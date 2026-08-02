namespace SonarCopilotFix.Interfaces;

public interface IGitHubCliService
{
    Task SetupGitAuthenticationAsync(CancellationToken cancellationToken);
    Task CreatePullRequestAsync(
        IPullRequestResult pullRequestResult,
        CancellationToken cancellationToken);
}
