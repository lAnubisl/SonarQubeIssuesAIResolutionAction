using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.SonarQube;

public sealed class SonarQubeClient : ISonarQubeClient, IDisposable
{
    private readonly ActionInputs _options;
    private readonly TextLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SonarQubeClient(ActionInputs options, TextLogger logger)
        : this(options, logger, new HttpClient(), disposeClient: true)
    {
    }

    public SonarQubeClient(ActionInputs options, TextLogger logger, HttpClient httpClient, bool disposeClient = false)
    {
        _options = options;
        _logger = logger;
        _httpClient = httpClient;
        _disposeClient = disposeClient;
        _httpClient.BaseAddress = options.SonarHostUrl;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.SonarToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken)
    {
        var selected = new List<SonarIssue>();
        var page = 1;
        var pageSize = Math.Min(_options.MaxIssues, 100);
        var total = 0;
        var issuesSeen = 0;

        while (selected.Count < _options.MaxIssues)
        {
            var uri = BuildIssueSearchUri(page, pageSize);
            var requestUrl = new Uri(_httpClient.BaseAddress!, uri);
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

                if (selected.Count >= _options.MaxIssues)
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

    private string BuildIssueSearchUri(int page, int pageSize)
    {
        var query = new Dictionary<string, string?>
        {
            ["componentKeys"] = _options.SonarProjectKey,
            ["p"] = page.ToString(),
            ["ps"] = pageSize.ToString()
        };

        if (!string.IsNullOrWhiteSpace(_options.SonarBranch))
        {
            query["branch"] = _options.SonarBranch;
        }

        if (!string.IsNullOrWhiteSpace(_options.SonarOrganization))
        {
            query["organization"] = _options.SonarOrganization;
        }

        if (_options.IssueStatuses.Count > 0)
        {
            query["statuses"] = string.Join(",", _options.IssueStatuses);
        }

        if (_options.Severities.Count > 0)
        {
            query["severities"] = string.Join(",", _options.Severities);
        }

        if (_options.CleanCodeAttributeCategories.Count > 0)
        {
            query["cleanCodeAttributeCategories"] = string.Join(",", _options.CleanCodeAttributeCategories);
        }

        return "/api/issues/search?" + string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
    }

    private async Task<SonarIssue> ToIssueAsync(IssueDto dto, CancellationToken cancellationToken)
    {
        var filePath = ExtractFilePath(dto.Component, _options.SonarProjectKey);
        SonarRule? rule = null;
        if (_options.IncludeRuleDetails && !string.IsNullOrWhiteSpace(dto.Rule))
        {
            rule = await TryGetRuleAsync(dto.Rule, cancellationToken);
        }

        return new SonarIssue(
            dto.Key ?? "unknown",
            dto.Rule ?? "unknown",
            dto.Severity ?? dto.Impacts?.FirstOrDefault()?.Severity,
            dto.Status,
            dto.Type,
            dto.CleanCodeAttributeCategory,
            dto.Component ?? "",
            filePath,
            dto.Line ?? dto.TextRange?.StartLine,
            dto.TextRange is null ? null : new TextRange(dto.TextRange.StartLine, dto.TextRange.EndLine, dto.TextRange.StartOffset, dto.TextRange.EndOffset),
            dto.Message ?? "",
            dto.Effort ?? dto.Debt,
            dto.Tags ?? [],
            dto.Author,
            BuildIssueUrl(dto.Key),
            rule,
            null,
            dto.Project,
            dto.Hash,
            dto.Flows?.Select(ToFlow).ToArray() ?? [],
            dto.Resolution,
            dto.Debt,
            ParseSonarDate(dto.CreationDate),
            ParseSonarDate(dto.UpdateDate),
            ParseSonarDate(dto.CloseDate),
            dto.Organization,
            dto.ExternalRuleEngine,
            dto.CleanCodeAttribute,
            dto.Impacts?.Select(impact => new SonarImpact(impact.SoftwareQuality, impact.Severity)).ToArray() ?? [],
            dto.IssueStatus,
            dto.ProjectName,
            dto.InternalTags ?? [],
            dto.LastChangeAnalysisUuid,
            dto.LastChangeSource);
    }

    private static DateTimeOffset? ParseSonarDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;

    private static SonarFlow ToFlow(FlowDto flow) =>
        new(flow.Locations?.Select(location => new SonarLocation(
            location.Component,
            location.TextRange is null
                ? null
                : new TextRange(
                    location.TextRange.StartLine,
                    location.TextRange.EndLine,
                    location.TextRange.StartOffset,
                    location.TextRange.EndOffset),
            location.Message)).ToArray() ?? []);

    private async Task<SonarRule?> TryGetRuleAsync(string ruleKey, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/api/rules/show?key={Uri.EscapeDataString(ruleKey)}", cancellationToken);
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
        var builder = new UriBuilder(_options.SonarHostUrl)
        {
            Path = "project/issues",
            Query = $"id={Uri.EscapeDataString(_options.SonarProjectKey)}&issues={Uri.EscapeDataString(issueKey ?? "")}&open={Uri.EscapeDataString(issueKey ?? "")}"
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

    private sealed record IssueSearchResponse(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("issues")] List<IssueDto> Issues);

    private sealed record IssueDto(
        string? Key,
        string? Rule,
        string? Severity,
        string? Status,
        string? Type,
        string? Component,
        string? Project,
        string? Hash,
        int? Line,
        TextRangeDto? TextRange,
        IReadOnlyList<FlowDto>? Flows,
        string? Resolution,
        string? Message,
        string? Effort,
        string? Debt,
        IReadOnlyList<string>? Tags,
        string? Author,
        string? CleanCodeAttributeCategory,
        string? CreationDate,
        string? UpdateDate,
        string? CloseDate,
        string? Organization,
        string? ExternalRuleEngine,
        string? CleanCodeAttribute,
        IReadOnlyList<ImpactDto>? Impacts,
        string? IssueStatus,
        string? ProjectName,
        IReadOnlyList<string>? InternalTags,
        string? LastChangeAnalysisUuid,
        string? LastChangeSource);

    private sealed record TextRangeDto(int StartLine, int EndLine, int StartOffset, int EndOffset);
    private sealed record FlowDto(IReadOnlyList<LocationDto>? Locations);
    private sealed record LocationDto(string? Component, TextRangeDto? TextRange, [property: JsonPropertyName("msg")] string? Message);
    private sealed record ImpactDto(string? SoftwareQuality, string? Severity);
    private sealed record RuleShowResponse(RuleDto? Rule);
    private sealed record RuleDto(string? Key, string? Name, string? HtmlDesc, string? MarkdownDescription, string? Severity, IReadOnlyList<string>? Tags);
}
