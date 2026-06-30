using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class GitHubCliService(
    CommandRunner commandRunner,
    IConfigurationHelper configurationHelper,
    ILogger logger)
{
    public async Task SetupGitAuthenticationAsync(CancellationToken cancellationToken)
    {
        var env = BuildEnvironment(configurationHelper);
        var result = await commandRunner.RunAsync(
            "gh",
            ["auth", "setup-git"],
            configurationHelper.GitHubWorkspace,
            env,
            cancellationToken,
            logCommandDetails: true);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException("GitHub CLI failed to configure git authentication.", ExitCodes.GitHubCliFailure);
        }
    }

    public async Task<string> CreatePullRequestAsync(
        string title,
        string bodyFile,
        string baseBranch,
        string headBranch,
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

    public static IReadOnlyDictionary<string, string?> BuildEnvironment(IConfigurationHelper configurationHelper)
    {
        // gh invokes git while creating a pull request. Docker actions operate on
        // a host-owned bind mount, so pass command-scoped Git configuration that
        // is inherited by gh's child processes without changing global config.
        return new Dictionary<string, string?>
        {
            ["GH_TOKEN"] = configurationHelper.GetEffectiveGitHubToken(),
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "safe.directory",
            ["GIT_CONFIG_VALUE_0"] = Path.GetFullPath(configurationHelper.GitHubWorkspace)
        };
    }
}
