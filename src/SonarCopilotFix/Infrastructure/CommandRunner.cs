using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SonarCopilotFix.Infrastructure;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Summary
    {
        get
        {
            var combined = string.Join(Environment.NewLine, [StandardOutput, StandardError]).Trim();
            if (combined.Length <= 4000)
            {
                return combined;
            }

            return combined[^4000..];
        }
    }
}

public sealed class CommandRunner(ILogger logger, IConfigurationHelper configurationHelper) : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        CancellationToken cancellationToken = default,
        Action<string>? standardOutputReceived = null,
        Action<string>? standardErrorReceived = null,
        bool logCommandDetails = false)
    {
        var psi = CreateBaseProcess(fileName, workingDirectory, scopedEnvironment);
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(psi, cancellationToken, standardOutputReceived, standardErrorReceived, logCommandDetails);
    }

    public async Task<CommandResult> RunShellAsync(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        CancellationToken cancellationToken = default)
    {
        var shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/sh";
        var shellArgs = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "/d", "/s", "/c", command }
            : new[] { "-c", command };
        return await RunAsync(shell, shellArgs, workingDirectory, scopedEnvironment, cancellationToken);
    }

    private ProcessStartInfo CreateBaseProcess(string fileName, string workingDirectory, IReadOnlyDictionary<string, string?>? scopedEnvironment)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment.Clear();
        foreach (var (key, value) in BuildSafeEnvironment(scopedEnvironment))
        {
            psi.Environment[key] = value;
        }

        return psi;
    }

    public IReadOnlyDictionary<string, string> BuildSafeEnvironment(IReadOnlyDictionary<string, string?>? scopedEnvironment)
    {
        var safeEnvironment = new Dictionary<string, string?>
        {
            ["PATH"] = configurationHelper.Path,
            ["HOME"] = configurationHelper.Home,
            ["USER"] = configurationHelper.User,
            ["USERPROFILE"] = configurationHelper.UserProfile,
            ["TMPDIR"] = configurationHelper.TmpDir,
            ["TEMP"] = configurationHelper.Temp,
            ["TMP"] = configurationHelper.Tmp,
            ["CI"] = configurationHelper.Ci,
            ["GITHUB_ACTIONS"] = configurationHelper.GitHubActions,
            ["GITHUB_WORKSPACE"] = configurationHelper.GitHubWorkspace,
            ["RUNNER_TEMP"] = configurationHelper.RunnerTemp,
            ["DOTNET_ROOT"] = configurationHelper.DotNetRoot,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = configurationHelper.DotNetCliTelemetryOptOut
        };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in safeEnvironment)
        {
            if (!string.IsNullOrEmpty(value))
            {
                result[name] = value;
            }
        }

        if (scopedEnvironment is not null)
        {
            foreach (var (key, value) in scopedEnvironment)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[key] = value!;
                }
            }
        }

        return result;
    }

    private async Task<CommandResult> RunProcessAsync(
        ProcessStartInfo psi,
        CancellationToken cancellationToken,
        Action<string>? standardOutputReceived,
        Action<string>? standardErrorReceived,
        bool logCommandDetails)
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
                if (logCommandDetails)
                {
                    logger.Info($"[{psi.FileName} stdout] {args.Data}");
                }

                standardOutputReceived?.Invoke(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
                if (logCommandDetails)
                {
                    logger.Info($"[{psi.FileName} stderr] {args.Data}");
                }

                standardErrorReceived?.Invoke(args.Data);
            }
        };

        logger.Info(logCommandDetails
            ? $"Starting command: {FormatCommand(psi)}"
            : $"Starting command '{psi.FileName}'.");
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start command '{psi.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        if (logCommandDetails)
        {
            if (stdout.Length == 0)
            {
                logger.Info($"[{psi.FileName} stdout] <empty>");
            }

            if (stderr.Length == 0)
            {
                logger.Info($"[{psi.FileName} stderr] <empty>");
            }

            logger.Info($"Command '{psi.FileName}' exited with code {process.ExitCode}.");
        }

        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string FormatCommand(ProcessStartInfo psi)
    {
        return string.Join(" ", new[] { QuoteArgument(psi.FileName) }.Concat(psi.ArgumentList.Select(QuoteArgument)));
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.All(character => !char.IsWhiteSpace(character) && character is not '"' and not '\''))
        {
            return argument;
        }

        return $"'{argument.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }
}
