namespace SonarCopilotFix.SonarQube;

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
    SonarRule? Rule,
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
    string? LastChangeSource = null);

public sealed record TextRange(int StartLine, int EndLine, int StartOffset, int EndOffset);
public sealed record SonarFlow(IReadOnlyList<SonarLocation> Locations);
public sealed record SonarLocation(string? Component, TextRange? TextRange, string? Message);
public sealed record SonarImpact(string? SoftwareQuality, string? Severity);
public sealed record SonarRule(string Key, string? Name, string? HtmlDescription, string? MarkdownDescription, string? Severity, IReadOnlyList<string> Tags);
public sealed record CodeSnippet(string FilePath, bool FileFound, int? StartLine, int? EndLine, string Content);
public sealed record SonarIssueSearchResult(int TotalFound, IReadOnlyList<SonarIssue> Issues);
