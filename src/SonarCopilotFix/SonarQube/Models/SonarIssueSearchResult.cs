namespace SonarCopilotFix.SonarQube.Models;

public sealed record SonarIssueSearchResult(int TotalFound, IReadOnlyList<SonarIssue> Issues);
