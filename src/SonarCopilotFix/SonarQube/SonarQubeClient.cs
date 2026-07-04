using System.Net;
using System.Text.Json;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.SonarQube;

public sealed class SonarQubeClient : ISonarQubeClient, IDisposable
{
    private readonly IConfigurationHelper _configurationHelper;
    private readonly ILogger _logger;
    private readonly ISonarQubeHttpClient _httpClient;
    private readonly ICodeSnippetReader _snippetReader;
    private readonly bool _disposeClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SonarQubeClient(
        IConfigurationHelper configurationHelper,
        ILogger logger,
        ISonarQubeHttpClient httpClient,
        ICodeSnippetReader snippetReader,
        bool disposeClient = false)
    {
        _configurationHelper = configurationHelper;
        _logger = logger;
        _httpClient = httpClient;
        _snippetReader = snippetReader;
        _disposeClient = disposeClient;
    }

    public async Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken)
    {
        var selected = new List<SonarIssue>();
        var page = 1;
        var pageSize = Math.Min(_configurationHelper.InputMaxIssues, 100);
        var total = 0;
        var issuesSeen = 0;

        while (selected.Count < _configurationHelper.InputMaxIssues)
        {
            var uri = BuildIssueSearchUri(page, pageSize);
            var requestUrl = new Uri(_httpClient.BaseAddress, uri);
            _logger.Info($"SonarQube issue search request URL: {requestUrl.AbsoluteUri}");

            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.Info($"SonarQube issue search response body: {responseBody}");

            EnsureSuccess(response, "search SonarQube issues", responseBody);
            var payload = Deserialize<IssueSearchResponse>(responseBody);
            total = payload.Total;

            foreach (var issue in payload.Issues)
            {
                _logger.Info($"SonarQube returned issue: key={issue.Key ?? "unknown"}, status={issue.IssueStatus ?? issue.Status ?? "not specified"}");
            }

            if (payload.Issues.Count == 0)
            {
                break;
            }

            foreach (var issue in payload.Issues)
            {
                issuesSeen++;

                if (selected.Count >= _configurationHelper.InputMaxIssues)
                {
                    break;
                }

                selected.Add(await ToIssueAsync(issue, cancellationToken));
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
        _configurationHelper.InputIncludeCodeSnippets
            ? _snippetReader.AddSnippets(issues)
            : issues;

    public IReadOnlyList<IssueGroup> GroupIssuesByRule(IReadOnlyList<SonarIssue> issues) =>
        issues
            .GroupBy(issue => issue.RuleKey, StringComparer.Ordinal)
            .Select(group => new IssueGroup(group.Key, group.ToArray()))
            .ToArray();

    private string BuildIssueSearchUri(int page, int pageSize)
    {
        var query = new Dictionary<string, string?>
        {
            ["componentKeys"] = _configurationHelper.InputComponents.Count > 0
                ? string.Join(",", _configurationHelper.InputComponents)
                : _configurationHelper.GetSonarProjectKey(),
            ["p"] = page.ToString(),
            ["ps"] = pageSize.ToString()
        };

        if (!string.IsNullOrWhiteSpace(_configurationHelper.InputSonarBranch))
        {
            query["branch"] = _configurationHelper.InputSonarBranch;
        }

        if (!string.IsNullOrWhiteSpace(_configurationHelper.InputSonarOrganization))
        {
            query["organization"] = _configurationHelper.InputSonarOrganization;
        }

        if (_configurationHelper.InputStatuses.Count > 0)
        {
            query["statuses"] = string.Join(",", _configurationHelper.InputStatuses);
        }

        if (!string.IsNullOrWhiteSpace(_configurationHelper.InputType))
        {
            query["types"] = _configurationHelper.InputType;
        }

        if (_configurationHelper.InputSeverities.Count > 0)
        {
            query["severities"] = string.Join(",", _configurationHelper.InputSeverities);
        }

        if (_configurationHelper.InputImpactSoftwareQualities.Count > 0)
        {
            query["impactSoftwareQualities"] = string.Join(",", _configurationHelper.InputImpactSoftwareQualities);
        }

        if (_configurationHelper.InputImpactSeverities.Count > 0)
        {
            query["impactSeverities"] = string.Join(",", _configurationHelper.InputImpactSeverities);
        }

        if (_configurationHelper.InputCleanCodeAttributeCategories.Count > 0)
        {
            query["cleanCodeAttributeCategories"] = string.Join(",", _configurationHelper.InputCleanCodeAttributeCategories);
        }

        if (_configurationHelper.InputRules.Count > 0)
        {
            query["rules"] = string.Join(",", _configurationHelper.InputRules);
        }

        return "/api/issues/search?" + string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
    }

    private async Task<SonarIssue> ToIssueAsync(IssueDto dto, CancellationToken cancellationToken)
    {
        var filePath = ExtractFilePath(dto.Component, _configurationHelper.GetSonarProjectKey());
        SonarRule? rule = null;
        if (_configurationHelper.InputIncludeRuleDetails && !string.IsNullOrWhiteSpace(dto.Rule))
        {
            rule = await TryGetRuleAsync(dto.Rule, cancellationToken);
        }

        return new SonarIssue(
            dto,
            filePath,
            BuildIssueUrl(dto.Key),
            rule);
    }

    private async Task<SonarRule?> TryGetRuleAsync(string ruleKey, CancellationToken cancellationToken)
    {
        string uri = $"/api/rules/show?key={Uri.EscapeDataString(ruleKey)}";
        var requestUrl = new Uri(_httpClient.BaseAddress, uri);
        _logger.Info($"SonarQube rule show request URL: {requestUrl.AbsoluteUri}");
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Warn($"Could not retrieve SonarQube rule details for '{ruleKey}'.");
            return null;
        }

        var payload = await DeserializeAsync<RuleShowResponse>(response, cancellationToken);
        return payload.Rule is null
            ? null
            : new SonarRule(
                payload.Rule.Key ?? ruleKey,
                payload.Rule.Name,
                payload.Rule.HtmlDesc,
                payload.Rule.MarkdownDescription,
                payload.Rule.Severity,
                payload.Rule.Tags ?? []);
    }

    private Uri BuildIssueUrl(string? issueKey)
    {
        var builder = new UriBuilder(_configurationHelper.GetSonarHostUri())
        {
            Path = "project/issues",
            Query = $"id={Uri.EscapeDataString(_configurationHelper.GetSonarProjectKey())}&issues={Uri.EscapeDataString(issueKey ?? "")}&open={Uri.EscapeDataString(issueKey ?? "")}"
        };
        return builder.Uri;
    }

    public static string ExtractFilePath(string? component, string projectKey)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return "";
        }

        var prefix = projectKey + ":";
        return component.StartsWith(prefix, StringComparison.Ordinal)
            ? component[prefix.Length..].Replace('\\', '/')
            : component.Replace('\\', '/');
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
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

        var status = response.StatusCode switch
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
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }
}
