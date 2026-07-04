using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class GitHubCliService(
    ICommandRunner commandRunner,
    IConfigurationHelper configurationHelper,
    ILogger logger,
    PrBodyBuilder prBodyBuilder)
{
    public async Task SetupGitAuthenticationAsync(CancellationToken cancellationToken)
    {
        var env = BuildEnvironment(configurationHelper);
        var result = await commandRunner.RunAsync(
            "gh",
            ["auth", "setup-git"],
            configurationHelper.GitHubWorkspace,
            env,
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException("GitHub CLI failed to configure git authentication.", ExitCodes.GitHubCliFailure);
        }
    }

    public async Task CreatePullRequestAsync(
        PullRequestSummary pullRequestSummary,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "pr", "create",
            "--title", $"Fix SonarQube rule {pullRequestSummary.IssueGroup.RuleKey} ({pullRequestSummary.IssueGroup.Issues.Count} issue(s))",
            "--body", prBodyBuilder.Build(pullRequestSummary),
            "--base", pullRequestSummary.BaseBranch,
            "--head", pullRequestSummary.GeneratedBranch
        };
        if (configurationHelper.InputPullRequestDraft)
        {
            args.Add("--draft");
        }

        var env = BuildEnvironment(configurationHelper);
        var result = await commandRunner.RunAsync(
            "gh",
            args,
            configurationHelper.GitHubWorkspace,
            env,
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            var detail = result.Summary;
            var message = "GitHub CLI failed to create a pull request.";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += $" {detail}";
            }

            throw new ControlledFailureException(message, ExitCodes.GitHubCliFailure);
        }

        pullRequestSummary.PullRequestUrl = result.StandardOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
        logger.Info($"Created pull request: {pullRequestSummary.PullRequestUrl}");
    }

    public static IReadOnlyDictionary<string, string?> BuildEnvironment(IConfigurationHelper configurationHelper)
    {
        // gh invokes git while creating a pull request. Pass command-scoped Git
        // configuration to its child processes without changing global config.
        return new Dictionary<string, string?>
        {
            ["GH_TOKEN"] = configurationHelper.GetGitHubToken(),
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "safe.directory",
            ["GIT_CONFIG_VALUE_0"] = Path.GetFullPath(configurationHelper.GitHubWorkspace)
        };
    }
}
