using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class CopilotCliRunner(CommandRunner commandRunner, string workspace, JsonLogger logger)
{
    public async Task RunAsync(ActionInputs options, string promptPath, CancellationToken cancellationToken)
    {
        var args = BuildArguments(options, promptPath);
        var env = new Dictionary<string, string?>
        {
            ["COPILOT_CLI_TOKEN"] = options.CopilotCliToken,
            ["GITHUB_COPILOT_TOKEN"] = options.CopilotCliToken
        };

        var result = await commandRunner.RunAsync("copilot", args, workspace, env, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException(
                "GitHub Copilot CLI failed or could not run non-interactively. Check COPILOT_CLI_TOKEN and ensure a supported 'copilot' executable is available in the Docker image.",
                ExitCodes.CopilotFailure);
        }

        logger.Info("GitHub Copilot CLI completed.");
    }

    private static IReadOnlyList<string> BuildArguments(ActionInputs options, string promptPath)
    {
        var args = new List<string>
        {
            "--non-interactive",
            "--prompt-file",
            promptPath,
            "--workspace",
            options.Workspace
        };

        if (!string.IsNullOrWhiteSpace(options.CopilotModel))
        {
            args.Add("--model");
            args.Add(options.CopilotModel);
        }

        if (options.CopilotAllowAllTools)
        {
            args.Add("--allow-all-tools");
        }

        return args;
    }
}
