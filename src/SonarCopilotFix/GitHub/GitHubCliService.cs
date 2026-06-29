using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class GitHubCliService(CommandRunner commandRunner, string workspace, JsonLogger logger)
{
    public async Task SetupGitAuthenticationAsync(string token, CancellationToken cancellationToken)
    {
        var env = new Dictionary<string, string?> { ["GH_TOKEN"] = token };
        var result = await commandRunner.RunAsync(
            "gh",
            ["auth", "setup-git"],
            workspace,
            env,
            cancellationToken,
            logCommandDetails: true);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException("GitHub CLI failed to configure git authentication.", ExitCodes.GitHubCliFailure);
        }
    }

    public async Task<string> CreatePullRequestAsync(
        string token,
        string title,
        string bodyFile,
        string baseBranch,
        string headBranch,
        bool draft,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "pr", "create",
            "--title", title,
            "--body-file", bodyFile,
            "--base", baseBranch,
            "--head", headBranch
        };
        if (draft)
        {
            args.Add("--draft");
        }

        var env = new Dictionary<string, string?> { ["GH_TOKEN"] = token };
        var result = await commandRunner.RunAsync(
            "gh",
            args,
            workspace,
            env,
            cancellationToken,
            logCommandDetails: true);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException("GitHub CLI failed to create a pull request.", ExitCodes.GitHubCliFailure);
        }

        var url = result.StandardOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
        logger.Info($"Created pull request: {url}");
        return url;
    }
}
