using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.SonarQube;

public interface ISonarQubeClient
{
    Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken);

    IReadOnlyList<SonarIssue> EnrichIssues(IReadOnlyList<SonarIssue> issues);

    IReadOnlyList<IssueGroup> GroupIssuesByRule(IReadOnlyList<SonarIssue> issues);
}
