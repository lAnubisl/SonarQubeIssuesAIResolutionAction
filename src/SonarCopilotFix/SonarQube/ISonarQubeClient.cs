using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.SonarQube;

public interface ISonarQubeClient
{
    Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken);

    IReadOnlyList<SonarIssue> EnrichIssues(IReadOnlyList<SonarIssue> issues);

    Task<IReadOnlyList<IssueGroup>> GroupIssuesByRuleAsync(
        IReadOnlyList<SonarIssue> issues,
        CancellationToken cancellationToken);
}
