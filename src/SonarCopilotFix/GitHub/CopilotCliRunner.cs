using System.ComponentModel;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class CopilotCliRunner(CommandRunner commandRunner, string workspace, TextLogger logger)
{
    public async Task RunAsync(ActionInputs options, string promptPath, CancellationToken cancellationToken)
    {
        var prompt = await File.ReadAllTextAsync(promptPath, cancellationToken);
        LogPrompt(prompt);
        var args = BuildArguments(options, prompt);
        var env = new Dictionary<string, string?>
        {
            ["COPILOT_GITHUB_TOKEN"] = options.CopilotCliToken,
            ["COPILOT_AUTO_UPDATE"] = "false"
        };

        CommandResult result;
        try
        {
            result = await commandRunner.RunAsync(
                "copilot",
                args,
                workspace,
                env,
                cancellationToken,
                line => logger.Info($"[copilot stdout] {line}"),
                line => logger.Info($"[copilot stderr] {line}"));
        }
        catch (Win32Exception ex)
        {
            throw new ControlledFailureException(
                $"GitHub Copilot CLI could not be started: {ex.Message}. Ensure the standalone 'copilot' executable is installed and available on PATH.",
                ExitCodes.CopilotFailure);
        }

        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException(
                $"GitHub Copilot CLI failed with exit code {result.ExitCode}. Check that COPILOT_CLI_TOKEN is a supported token with the Copilot Requests permission. {result.Summary}",
                ExitCodes.CopilotFailure);
        }

        logger.Info("GitHub Copilot CLI completed.");
    }

    private void LogPrompt(string prompt)
    {
        logger.Info("GitHub Copilot CLI prompt follows.");
        foreach (var line in prompt.ReplaceLineEndings("\n").Split('\n'))
        {
            logger.Info($"[copilot prompt] {line}");
        }

        logger.Info("End GitHub Copilot CLI prompt.");
    }

    public static IReadOnlyList<string> BuildArguments(ActionInputs options, string prompt)
    {
        var args = new List<string>
        {
            "--prompt",
            prompt,
            "--no-ask-user"
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
        else
        {
            args.Add("--allow-tool=write");
        }

        return args;
    }
}
