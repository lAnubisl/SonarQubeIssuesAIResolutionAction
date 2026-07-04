namespace SonarCopilotFix.SonarQube.Models;

public sealed record IssueGroup(
    string RuleKey,
    IReadOnlyList<SonarIssue> Issues,
    SonarRule? Rule = null);
