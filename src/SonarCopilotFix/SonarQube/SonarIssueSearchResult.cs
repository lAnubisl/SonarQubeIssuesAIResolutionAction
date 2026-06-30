namespace SonarCopilotFix.SonarQube;

public sealed record SonarIssueSearchResult(int TotalFound, IReadOnlyList<SonarIssue> Issues);
