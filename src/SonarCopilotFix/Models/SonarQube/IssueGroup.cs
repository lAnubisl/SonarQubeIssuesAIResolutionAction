namespace SonarCopilotFix.Models.SonarQube;

public sealed record IssueGroup(
    string RuleKey,
    IReadOnlyList<SonarIssue> Issues,
    SonarRule? Rule = null);
