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
        ProcessStartInfo psi = CreateBaseProcess(fileName, workingDirectory, scopedEnvironment);
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(psi, standardOutputReceived, standardErrorReceived, cancellationToken);
    }

    private ProcessStartInfo CreateBaseProcess(string fileName, string workingDirectory, IReadOnlyDictionary<string, string?>? scopedEnvironment)
    {
        ProcessStartInfo psi = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment.Clear();
        foreach ((string? key, string? value) in BuildSafeEnvironment(scopedEnvironment))
        {
            psi.Environment[key] = value;
        }

        return psi;
    }

    public IReadOnlyDictionary<string, string> BuildSafeEnvironment(IReadOnlyDictionary<string, string?>? scopedEnvironment)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach ((string? name, string? value) in configurationHelper.SafeEnvironmentVariables)
        {
            if (!string.IsNullOrEmpty(value))
            {
                result[name] = value;
            }
        }

        if (scopedEnvironment is not null)
        {
            foreach ((string? key, string? value) in scopedEnvironment)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[key] = value;
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
        using Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, args) => HandleData(args, stdout, standardOutputReceived);
        process.ErrorDataReceived += (_, args) => HandleData(args, stderr, standardErrorReceived);

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

    private static void HandleData(
        DataReceivedEventArgs args,
        StringBuilder destination,
        Action<string>? dataReceived)
    {
        if (args.Data is null)
        {
            return;
        }

        destination.AppendLine(args.Data);
        dataReceived?.Invoke(args.Data);
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
