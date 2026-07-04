using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SonarCopilotFix.Infrastructure.Models;

namespace SonarCopilotFix.Infrastructure;

public sealed class CommandRunner(ILogger logger, IConfigurationHelper configurationHelper) : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        Action<string>? standardOutputReceived = null,
        Action<string>? standardErrorReceived = null,
        CancellationToken cancellationToken = default)
    {
        var psi = CreateBaseProcess(fileName, workingDirectory, scopedEnvironment);
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(psi, standardOutputReceived, standardErrorReceived, cancellationToken);
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
        return await RunAsync(shell, shellArgs, workingDirectory, scopedEnvironment, cancellationToken: cancellationToken);
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
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in configurationHelper.SafeEnvironmentVariables)
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
        Action<string>? standardOutputReceived,
        Action<string>? standardErrorReceived,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) => HandleOutputData(args, stdout, standardOutputReceived);
        process.ErrorDataReceived += (_, args) => HandleErrorData(args, stderr, standardErrorReceived);

        LogStartingCommand(psi);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start command '{psi.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        LogCompletedCommand(psi, process.ExitCode);

        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void HandleOutputData(DataReceivedEventArgs args, StringBuilder stdout, Action<string>? standardOutputReceived)
    {
        if (args.Data is not null)
        {
            stdout.AppendLine(args.Data);
            standardOutputReceived?.Invoke(args.Data);
        }
    }

    private static void HandleErrorData(DataReceivedEventArgs args, StringBuilder stderr, Action<string>? standardErrorReceived)
    {
        if (args.Data is not null)
        {
            stderr.AppendLine(args.Data);
            standardErrorReceived?.Invoke(args.Data);
        }
    }

    private void LogStartingCommand(ProcessStartInfo psi)
    {
        logger.Info($"Starting command '{FormatCommand(psi)}'.");
    }

    private void LogCompletedCommand(ProcessStartInfo psi, int exitCode)
    {
        logger.Info($"Command '{psi.FileName}' exited with code {exitCode}.");
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
