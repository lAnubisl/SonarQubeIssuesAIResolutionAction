using System.Globalization;

namespace SonarCopilotFix.Models.SonarQube;

public sealed record SonarIssue(
    string Key,
    string RuleKey,
    string? Severity,
    string? Status,
    string? Type,
    string? CleanCodeAttributeCategory,
    string Component,
    string FilePath,
    int? Line,
    TextRange? TextRange,
    string Message,
    string? Effort,
    IReadOnlyList<string> Tags,
    string? Author,
    Uri IssueUrl,
    CodeSnippet? CodeSnippet,
    string? Project = null,
    string? Hash = null,
    IReadOnlyList<SonarFlow>? Flows = null,
    string? Resolution = null,
    string? Debt = null,
    DateTimeOffset? CreationDate = null,
    DateTimeOffset? UpdateDate = null,
    DateTimeOffset? CloseDate = null,
    string? Organization = null,
    string? ExternalRuleEngine = null,
    string? CleanCodeAttribute = null,
    IReadOnlyList<SonarImpact>? Impacts = null,
    string? IssueStatus = null,
    string? ProjectName = null,
    IReadOnlyList<string>? InternalTags = null,
    string? LastChangeAnalysisUuid = null,
    string? LastChangeSource = null)
{
    internal SonarIssue(IssueDto dto, string filePath, Uri issueUrl)
        : this(
            dto.Key ?? "unknown",
            dto.Rule ?? "unknown",
            dto.Severity ?? dto.Impacts?.FirstOrDefault()?.Severity,
            dto.Status,
            dto.Type,
            dto.CleanCodeAttributeCategory,
            dto.Component ?? "",
            filePath,
            dto.Line ?? dto.TextRange?.StartLine,
            ToTextRange(dto.TextRange),
            dto.Message ?? "",
            dto.Effort ?? dto.Debt,
            dto.Tags ?? [],
            dto.Author,
            issueUrl,
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
            dto.LastChangeSource)
    {
    }

    private static DateTimeOffset? ParseSonarDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset result)
            ? result
            : null;

    private static SonarFlow ToFlow(FlowDto flow) =>
        new(flow.Locations?.Select(location => new SonarLocation(
            location.Component,
            ToTextRange(location.TextRange),
            location.Message)).ToArray() ?? []);

    private static TextRange? ToTextRange(TextRangeDto? textRange) =>
        textRange is null
            ? null
            : new TextRange(
                textRange.StartLine,
                textRange.EndLine,
                textRange.StartOffset,
                textRange.EndOffset);
}
