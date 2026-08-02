namespace SonarCopilotFix.Models.SonarQube;

public sealed record SonarIssueSearchResult(int TotalFound, IReadOnlyList<SonarIssue> Issues);
