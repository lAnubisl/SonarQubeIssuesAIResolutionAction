using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Interfaces;

public interface ISonarQubeClient : IDisposable
{
    Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken);

    IReadOnlyList<SonarIssue> EnrichIssues(IReadOnlyList<SonarIssue> issues);

    Task<IReadOnlyList<IssueGroup>> GroupIssuesByRuleAsync(
        IReadOnlyList<SonarIssue> issues,
        CancellationToken cancellationToken);
}
