using System.ComponentModel;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;

namespace SonarCopilotFix.GitHub;

public sealed class CopilotCliRunner(
    ICommandRunner commandRunner,
    IConfigurationHelper configurationHelper,
    ILogger logger) : ICopilotCliRunner
{
    private static readonly string[] DefaultWriteTools = ["write"];
    private const string DenyGitCommitTool = "shell(git commit)";

    public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        string sessionId = Guid.NewGuid().ToString();
        string gitHookDirectory = await CreateGitCommitGuardAsync(cancellationToken);
        Dictionary<string, string?> environment = BuildEnvironment(configurationHelper, gitHookDirectory);
        CommandResult result = await ExecutePromptAsync(prompt, sessionId, environment, cancellationToken);
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
            string authenticationGuidance = string.IsNullOrWhiteSpace(configurationHelper.InputCopilotProviderBaseUrl)
                ? "Check that COPILOT_GITHUB_TOKEN is a supported token with the Copilot Requests permission."
                : "Check the custom provider URL, model, provider type, and COPILOT_PROVIDER_API_KEY when the provider requires authentication.";
            throw new ControlledFailureException(
                $"GitHub Copilot CLI failed with exit code {result.ExitCode}. {authenticationGuidance} {result.Summary}",
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
        string gitHookDirectory)
    {
        Dictionary<string, string?> environment = new()
        {
            ["COPILOT_AUTO_UPDATE"] = "false",
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "core.hooksPath",
            ["GIT_CONFIG_VALUE_0"] = gitHookDirectory
        };

        if (!string.IsNullOrWhiteSpace(configurationHelper.CopilotGitHubToken))
        {
            environment["COPILOT_GITHUB_TOKEN"] = configurationHelper.CopilotGitHubToken;
        }

        if (!string.IsNullOrWhiteSpace(configurationHelper.InputCopilotProviderBaseUrl))
        {
            environment["COPILOT_PROVIDER_BASE_URL"] = configurationHelper.InputCopilotProviderBaseUrl;
            environment["COPILOT_MODEL"] = configurationHelper.InputCopilotModel;
            environment["COPILOT_PROVIDER_TYPE"] = configurationHelper.InputCopilotProviderType;
            environment["COPILOT_PROVIDER_API_KEY"] = configurationHelper.CopilotProviderApiKey;
        }

        if (configurationHelper.InputCopilotOffline)
        {
            environment["COPILOT_OFFLINE"] = "true";
        }

        return environment;
    }

    private async Task<string> CreateGitCommitGuardAsync(CancellationToken cancellationToken)
    {
        string hookDirectory = Path.GetFullPath(
            Path.Combine(configurationHelper.GitHubWorkspace, ".sonar-copilot", "copilot-git-hooks"));
        Directory.CreateDirectory(hookDirectory);
        string hookPath = Path.Combine(hookDirectory, "pre-commit");
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

    public static IReadOnlyList<string> BuildArguments(
        IConfigurationHelper configurationHelper,
        string prompt,
        string? sessionId = null)
    {
        List<string> args =
        [
            "--prompt",
            prompt,
            "--no-ask-user",
            "--no-color"
        ];

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
            IReadOnlyList<string> allowedTools = configurationHelper.InputCopilotAllowedTools;
            foreach (string pattern in allowedTools)
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
        int openParenthesis = pattern.IndexOf('(');
        string kind = openParenthesis < 0 ? pattern : pattern[..openParenthesis];
        bool validKind = kind.Length > 0
            && kind.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
        bool validArgument = openParenthesis < 0
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
