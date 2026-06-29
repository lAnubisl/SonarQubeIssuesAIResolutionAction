using System.ComponentModel;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class CopilotCliRunner(CommandRunner commandRunner, string workspace, TextLogger logger)
{
    public async Task<string> RunAsync(ActionInputs options, string promptPath, CancellationToken cancellationToken)
    {
        var prompt = await File.ReadAllTextAsync(promptPath, cancellationToken);
        LogPrompt(prompt);

        var sessionId = Guid.NewGuid().ToString();
        var environment = BuildEnvironment(options);

        await ExecutePromptAsync(options, prompt, sessionId, environment, cancellationToken);
        await LogSessionInfoAsync(sessionId, environment, cancellationToken);
        return await GetUsageReportAsync(sessionId, environment, cancellationToken);
    }

    private async Task ExecutePromptAsync(
        ActionInputs options,
        string prompt,
        string sessionId,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        CommandResult result;
        try
        {
            result = await RunCommandAsync(
                BuildArguments(options, prompt, sessionId),
                environment,
                "copilot",
                cancellationToken);
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

    private async Task LogSessionInfoAsync(
        string sessionId,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        logger.Info($"Querying Copilot CLI information for session {sessionId}.");
        var result = await RunCommandAsync(
            BuildSessionArguments(sessionId),
            environment,
            "copilot session",
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException(
                $"GitHub Copilot CLI failed to report information for session {sessionId}. {result.Summary}",
                ExitCodes.CopilotFailure);
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new ControlledFailureException(
                $"GitHub Copilot CLI returned empty information for session {sessionId}.",
                ExitCodes.CopilotFailure);
        }
    }

    private async Task<string> GetUsageReportAsync(
        string sessionId,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        logger.Info($"Querying Copilot CLI usage for session {sessionId}.");
        var result = await RunCommandAsync(
            BuildUsageArguments(sessionId),
            environment,
            "copilot usage",
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException(
                $"GitHub Copilot CLI failed to report usage for session {sessionId}. {result.Summary}",
                ExitCodes.CopilotFailure);
        }

        var usageReport = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(usageReport))
        {
            throw new ControlledFailureException(
                $"GitHub Copilot CLI returned an empty usage report for session {sessionId}.",
                ExitCodes.CopilotFailure);
        }

        return usageReport;
    }

    private Task<CommandResult> RunCommandAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        string logPrefix,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "copilot",
            arguments,
            workspace,
            environment,
            cancellationToken,
            line => logger.Info($"[{logPrefix} stdout] {line}"),
            line => logger.Info($"[{logPrefix} stderr] {line}"));

    private static IReadOnlyDictionary<string, string?> BuildEnvironment(ActionInputs options) =>
        new Dictionary<string, string?>
        {
            ["COPILOT_GITHUB_TOKEN"] = options.CopilotCliToken,
            ["COPILOT_AUTO_UPDATE"] = "false"
        };

    private void LogPrompt(string prompt)
    {
        logger.Info("GitHub Copilot CLI prompt follows.");
        foreach (var line in prompt.ReplaceLineEndings("\n").Split('\n'))
        {
            logger.Info($"[copilot prompt] {line}");
        }

        logger.Info("End GitHub Copilot CLI prompt.");
    }

    public static IReadOnlyList<string> BuildArguments(ActionInputs options, string prompt, string? sessionId = null)
    {
        var args = new List<string>
        {
            "--prompt",
            prompt,
            "--no-ask-user",
            "--no-color"
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            args.Add("--session-id");
            args.Add(sessionId);
        }

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

    public static IReadOnlyList<string> BuildUsageArguments(string sessionId) =>
        ["--session-id", sessionId, "--prompt", "/usage", "--no-ask-user", "--no-color"];

    public static IReadOnlyList<string> BuildSessionArguments(string sessionId) =>
        ["--session-id", sessionId, "--prompt", "/session", "--no-ask-user", "--no-color"];
}
