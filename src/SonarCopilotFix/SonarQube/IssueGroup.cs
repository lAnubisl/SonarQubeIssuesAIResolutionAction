namespace SonarCopilotFix.SonarQube;

public sealed record IssueGroup(string RuleKey, IReadOnlyList<SonarIssue> Issues);
