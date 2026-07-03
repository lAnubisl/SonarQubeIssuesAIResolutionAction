using System.ComponentModel;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class CopilotCliRunner(
    CommandRunner commandRunner,
    IConfigurationHelper configurationHelper,
    ILogger logger)
{
    private static readonly string[] DefaultWriteTools = ["write"];
    private const string DenyGitCommitTool = "shell(git commit)";

    public async Task<string> RunAsync(string promptPath, CancellationToken cancellationToken)
    {
        var prompt = await File.ReadAllTextAsync(promptPath, cancellationToken);
        LogPrompt(prompt);

        var sessionId = Guid.NewGuid().ToString();
        var gitHookDirectory = await CreateGitCommitGuardAsync(cancellationToken);
        var environment = BuildEnvironment(configurationHelper, gitHookDirectory);

        var result = await ExecutePromptAsync(prompt, sessionId, environment, cancellationToken);
        return result.StandardError.Trim();
    }

    private async Task<CommandResult> ExecutePromptAsync(
        string prompt,
        string sessionId,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        CommandResult result;
        try
        {
            result = await RunCommandAsync(
                BuildArguments(configurationHelper, prompt, sessionId),
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
        return result;
    }

    private Task<CommandResult> RunCommandAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        string logPrefix,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "copilot",
            arguments,
            configurationHelper.GitHubWorkspace,
            environment,
            line => logger.Info($"[{logPrefix} stdout] {line}"),
            line => logger.Info($"[{logPrefix} stderr] {line}"),
            cancellationToken);

    private static Dictionary<string, string?> BuildEnvironment(
        IConfigurationHelper configurationHelper,
        string gitHookDirectory) =>
        new Dictionary<string, string?>
        {
            ["COPILOT_GITHUB_TOKEN"] = configurationHelper.CopilotCliToken,
            ["COPILOT_AUTO_UPDATE"] = "false",
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "core.hooksPath",
            ["GIT_CONFIG_VALUE_0"] = gitHookDirectory
        };

    private async Task<string> CreateGitCommitGuardAsync(CancellationToken cancellationToken)
    {
        var hookDirectory = Path.GetFullPath(
            Path.Combine(configurationHelper.GitHubWorkspace, ".sonar-copilot", "copilot-git-hooks"));
        Directory.CreateDirectory(hookDirectory);
        var hookPath = Path.Combine(hookDirectory, "pre-commit");
        await File.WriteAllTextAsync(
            hookPath,
            "#!/bin/sh\n"
            + "echo \"git commit is disabled during the Copilot session; leave changes uncommitted for the outer workflow.\" >&2\n"
            + "exit 1\n",
            cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return hookDirectory;
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

    public static IReadOnlyList<string> BuildArguments(
        IConfigurationHelper configurationHelper,
        string prompt,
        string? sessionId = null)
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

        if (!string.IsNullOrWhiteSpace(configurationHelper.InputCopilotModel))
        {
            args.Add("--model");
            args.Add(configurationHelper.InputCopilotModel);
        }

        if (configurationHelper.InputCopilotAllowAllTools)
        {
            args.Add("--allow-all-tools");
        }
        else
        {
            var allowedTools = configurationHelper.InputCopilotAllowedTools;
            foreach (var pattern in allowedTools)
            {
                ValidateToolPattern(pattern);
            }

            args.Add($"--allow-tool={string.Join(',', DefaultWriteTools.Concat(allowedTools))}");
        }

        // Denials take precedence over allow-all and any broad shell permission.
        args.Add($"--deny-tool={DenyGitCommitTool}");
        return args;
    }

    private static void ValidateToolPattern(string pattern)
    {
        var openParenthesis = pattern.IndexOf('(');
        var kind = openParenthesis < 0 ? pattern : pattern[..openParenthesis];
        var validKind = kind.Length > 0
            && kind.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
        var validArgument = openParenthesis < 0
            || openParenthesis < pattern.Length - 2
            && pattern.EndsWith(')')
            && pattern.AsSpan(openParenthesis + 1, pattern.Length - openParenthesis - 2)
                .IndexOfAny("()\r\n") < 0;

        if (!validKind || !validArgument)
        {
            throw new ControlledFailureException(
                $"Invalid Copilot tool permission pattern '{pattern}'. Expected a tool kind or Kind(argument), such as 'shell(dotnet:*)'.",
                ExitCodes.ConfigurationError);
        }
    }
}
