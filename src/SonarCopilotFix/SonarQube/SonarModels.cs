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
    CodeSnippet? CodeSnippet);

public sealed record TextRange(int StartLine, int EndLine, int StartOffset, int EndOffset);
public sealed record SonarRule(string Key, string? Name, string? HtmlDescription, string? MarkdownDescription, string? Severity, IReadOnlyList<string> Tags);
public sealed record CodeSnippet(string FilePath, bool FileFound, int? StartLine, int? EndLine, string Content);
public sealed record SonarIssueSearchResult(int TotalFound, IReadOnlyList<SonarIssue> Issues);
