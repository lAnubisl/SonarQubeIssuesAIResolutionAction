namespace SonarCopilotFix.SonarQube;

public interface ISonarQubeClient
{
    Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken);
}
