namespace SonarCopilotFix.GitHub;

public interface IGitHubCliService
{
    Task SetupGitAuthenticationAsync(CancellationToken cancellationToken);
    Task CreatePullRequestAsync(
        PullRequestSummary pullRequestSummary,
        CancellationToken cancellationToken);
}
