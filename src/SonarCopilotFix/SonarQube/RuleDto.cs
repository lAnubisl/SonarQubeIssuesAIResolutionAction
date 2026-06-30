namespace SonarCopilotFix.SonarQube;

internal sealed record RuleDto(
    string? Key,
    string? Name,
    string? HtmlDesc,
    string? MarkdownDescription,
    string? Severity,
    IReadOnlyList<string>? Tags);
