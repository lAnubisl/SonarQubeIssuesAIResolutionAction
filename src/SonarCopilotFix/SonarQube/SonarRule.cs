namespace SonarCopilotFix.SonarQube;

public sealed record SonarRule(
    string Key,
    string? Name,
    string? HtmlDescription,
    string? MarkdownDescription,
    string? Severity,
    IReadOnlyList<string> Tags);
