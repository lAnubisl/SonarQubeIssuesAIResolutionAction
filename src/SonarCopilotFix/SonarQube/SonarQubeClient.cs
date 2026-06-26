using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.SonarQube;

public sealed class SonarQubeClient : ISonarQubeClient, IDisposable
{
    private readonly ActionInputs _options;
    private readonly JsonLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SonarQubeClient(ActionInputs options, JsonLogger logger)
        : this(options, logger, new HttpClient(), disposeClient: true)
    {
    }

    public SonarQubeClient(ActionInputs options, JsonLogger logger, HttpClient httpClient, bool disposeClient = false)
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

        while (selected.Count < _options.MaxIssues)
        {
            var uri = BuildIssueSearchUri(page, pageSize);
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            await EnsureSuccessAsync(response, "search SonarQube issues", cancellationToken);
            var payload = await DeserializeAsync<IssueSearchResponse>(response, cancellationToken);
            total = payload.Total;

            if (payload.Issues.Count == 0)
            {
                break;
            }

            foreach (var issue in payload.Issues)
            {
                if (selected.Count >= _options.MaxIssues)
                {
                    break;
                }

                selected.Add(await ToIssueAsync(issue, cancellationToken));
            }

            if (selected.Count >= total)
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
            dto.Line,
            dto.TextRange is null ? null : new TextRange(dto.TextRange.StartLine, dto.TextRange.EndLine, dto.TextRange.StartOffset, dto.TextRange.EndOffset),
            dto.Message ?? "",
            dto.Effort ?? dto.Debt,
            dto.Tags ?? [],
            dto.Author,
            BuildIssueUrl(dto.Key),
            rule,
            null);
    }

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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
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

        throw new ControlledFailureException($"Failed to {operation}. {status} Response body length: {body.Length}.", ExitCodes.SonarQubeError);
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
        int? Line,
        TextRangeDto? TextRange,
        string? Message,
        string? Effort,
        string? Debt,
        IReadOnlyList<string>? Tags,
        string? Author,
        string? CleanCodeAttributeCategory,
        IReadOnlyList<ImpactDto>? Impacts);

    private sealed record TextRangeDto(int StartLine, int EndLine, int StartOffset, int EndOffset);
    private sealed record ImpactDto(string? SoftwareQuality, string? Severity);
    private sealed record RuleShowResponse(RuleDto? Rule);
    private sealed record RuleDto(string? Key, string? Name, string? HtmlDesc, string? MarkdownDescription, string? Severity, IReadOnlyList<string>? Tags);
}
