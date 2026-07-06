using System.Net;
using System.Text.Json;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.SonarQube;

public sealed class SonarQubeClient(
    IConfigurationHelper configurationHelper,
    ILogger logger,
    ISonarQubeHttpClient httpClient,
    ICodeSnippetReader snippetReader,
    bool disposeClient = false) : ISonarQubeClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken)
    {
        List<SonarIssue> selected = [];
        int page = 1;
        int pageSize = Math.Min(configurationHelper.InputMaxIssues, 100);
        int total = 0;
        int issuesSeen = 0;

        while (selected.Count < configurationHelper.InputMaxIssues)
        {
            string uri = BuildIssueSearchUri(page, pageSize);
            Uri requestUrl = new(httpClient.BaseAddress, uri);
            logger.Info($"SonarQube issue search request URL: {requestUrl.AbsoluteUri}");

            using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.Info($"SonarQube issue search response body: {responseBody}");

            EnsureSuccess(response, "search SonarQube issues", responseBody);
            IssueSearchResponse payload = Deserialize<IssueSearchResponse>(responseBody);
            total = payload.Total;

            foreach (IssueDto issue in payload.Issues)
            {
                logger.Info($"SonarQube returned issue: key={issue.Key ?? "unknown"}, status={issue.IssueStatus ?? issue.Status ?? "not specified"}");
            }

            if (payload.Issues.Count == 0)
            {
                break;
            }

            foreach (IssueDto issue in payload.Issues)
            {
                issuesSeen++;

                if (selected.Count >= configurationHelper.InputMaxIssues)
                {
                    break;
                }

                selected.Add(ToIssue(issue));
            }

            if (issuesSeen >= total)
            {
                break;
            }

            page++;
        }

        return new SonarIssueSearchResult(total, selected);
    }

    public IReadOnlyList<SonarIssue> EnrichIssues(IReadOnlyList<SonarIssue> issues) =>
        configurationHelper.InputIncludeCodeSnippets
            ? snippetReader.AddSnippets(issues)
            : issues;

    public async Task<IReadOnlyList<IssueGroup>> GroupIssuesByRuleAsync(
        IReadOnlyList<SonarIssue> issues,
        CancellationToken cancellationToken)
    {
        IGrouping<string, SonarIssue>[] groups = issues
            .GroupBy(issue => issue.RuleKey, StringComparer.Ordinal)
            .ToArray();

        List<IssueGroup> issueGroups = new(groups.Length);
        foreach (IGrouping<string, SonarIssue> group in groups)
        {
            SonarRule? rule = configurationHelper.InputIncludeRuleDetails
                && !string.Equals(group.Key, "unknown", StringComparison.Ordinal)
                ? await TryGetRuleAsync(group.Key, cancellationToken)
                : null;
            issueGroups.Add(new IssueGroup(group.Key, group.ToArray(), rule));
        }

        return issueGroups;
    }

    private static void AddQueryParameter(Dictionary<string, string?> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query[key] = value;
        }
    }

    private static void AddQueryParameter(Dictionary<string, string?> query, string key, IReadOnlyList<string>? value)
    {
        if (value?.Count > 0)
        {
            query[key] = string.Join(",", value);
        }
    }

    private string BuildIssueSearchUri(int page, int pageSize)
    {
        Dictionary<string, string?> query = new()
        {
            ["componentKeys"] = configurationHelper.InputComponents.Count > 0
                ? string.Join(",", configurationHelper.InputComponents)
                : configurationHelper.GetSonarProjectKey(),
            ["p"] = page.ToString(),
            ["ps"] = pageSize.ToString()
        };

        AddQueryParameter(query, "branch", configurationHelper.InputSonarBranch);
        AddQueryParameter(query, "organization", configurationHelper.InputSonarOrganization);
        AddQueryParameter(query, "statuses", configurationHelper.InputStatuses);
        AddQueryParameter(query, "types", configurationHelper.InputType);
        AddQueryParameter(query, "severities", configurationHelper.InputSeverities);
        AddQueryParameter(query, "impactSoftwareQualities", configurationHelper.InputImpactSoftwareQualities);
        AddQueryParameter(query, "impactSeverities", configurationHelper.InputImpactSeverities);
        AddQueryParameter(query, "cleanCodeAttributeCategories", configurationHelper.InputCleanCodeAttributeCategories);
        AddQueryParameter(query, "rules", configurationHelper.InputRules);

        return "api/issues/search?" + string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
    }

    private SonarIssue ToIssue(IssueDto dto)
    {
        string filePath = ExtractFilePath(dto.Component, configurationHelper.GetSonarProjectKey());
        return new SonarIssue(
            dto,
            filePath,
            BuildIssueUrl(dto.Key));
    }

    private async Task<SonarRule?> TryGetRuleAsync(string ruleKey, CancellationToken cancellationToken)
    {
        string uri = $"api/rules/show?key={Uri.EscapeDataString(ruleKey)}";
        if (!string.IsNullOrWhiteSpace(configurationHelper.InputSonarOrganization))
        {
            uri += $"&organization={Uri.EscapeDataString(configurationHelper.InputSonarOrganization)}";
        }

        Uri requestUrl = new(httpClient.BaseAddress, uri);
        logger.Info($"SonarQube rule show request URL: {requestUrl.AbsoluteUri}");
        using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.Warn($"Could not retrieve SonarQube rule details for '{ruleKey}'.");
            return null;
        }

        RuleShowResponse payload = await DeserializeAsync<RuleShowResponse>(response, cancellationToken);
        return payload.Rule is null
            ? null
            : new SonarRule(
                MapRuleDescription(payload.Rule),
                payload.Rule.Name);
    }

    private static IReadOnlyList<SonarRuleDescriptionSection> MapRuleDescription(RuleDto rule)
    {
        SonarRuleDescriptionSection[] sections = rule.DescriptionSections?
            .Select(section => new SonarRuleDescriptionSection(section.Key, section.Content))
            .ToArray()
            ?? [];
        if (sections.Length > 0)
        {
            return sections;
        }

        string? legacyDescription = !string.IsNullOrWhiteSpace(rule.MdDesc)
            ? rule.MdDesc
            : rule.HtmlDesc;
        return string.IsNullOrWhiteSpace(legacyDescription)
            ? []
            : [new SonarRuleDescriptionSection("description", legacyDescription)];
    }

    private Uri BuildIssueUrl(string? issueKey)
    {
        UriBuilder builder = new(new Uri(configurationHelper.GetSonarHostUri(), "project/issues"))
        {
            Query = $"id={Uri.EscapeDataString(configurationHelper.GetSonarProjectKey())}&issues={Uri.EscapeDataString(issueKey ?? "")}&open={Uri.EscapeDataString(issueKey ?? "")}"
        };
        return builder.Uri;
    }

    public static string ExtractFilePath(string? component, string projectKey)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return "";
        }

        string prefix = projectKey + ":";
        return component.StartsWith(prefix, StringComparison.Ordinal)
            ? component[prefix.Length..].Replace('\\', '/')
            : component.Replace('\\', '/');
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new ControlledFailureException("SonarQube returned an empty or malformed JSON response.", ExitCodes.SonarQubeError);
        }
        catch (JsonException ex)
        {
            throw new ControlledFailureException($"SonarQube returned a malformed JSON response: {ex.Message}", ExitCodes.SonarQubeError);
        }
    }

    private static T Deserialize<T>(string responseBody)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
                ?? throw new ControlledFailureException("SonarQube returned an empty or malformed JSON response.", ExitCodes.SonarQubeError);
        }
        catch (JsonException ex)
        {
            throw new ControlledFailureException($"SonarQube returned a malformed JSON response: {ex.Message}", ExitCodes.SonarQubeError);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation, string responseBody)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string status = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Malformed request or unsupported SonarQube filter.",
            HttpStatusCode.Unauthorized => "Invalid or missing SonarQube token.",
            HttpStatusCode.Forbidden => "SonarQube token lacks permission.",
            HttpStatusCode.NotFound => "SonarQube project or endpoint was not found.",
            (HttpStatusCode)429 => "SonarQube rate limit was reached.",
            HttpStatusCode.ServiceUnavailable => "SonarQube is unavailable or indexing is in progress.",
            _ => $"Unexpected SonarQube status {(int)response.StatusCode}."
        };

        throw new ControlledFailureException($"Failed to {operation}. {status} Response body length: {responseBody.Length}.", ExitCodes.SonarQubeError);
    }

    public void Dispose()
    {
        if (disposeClient)
        {
            httpClient.Dispose();
        }
    }
}
