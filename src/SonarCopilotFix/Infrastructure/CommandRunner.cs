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

public sealed class CommandRunner(JsonLogger logger)
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        CancellationToken cancellationToken = default,
        Action<string>? standardOutputReceived = null,
        Action<string>? standardErrorReceived = null)
    {
        var psi = CreateBaseProcess(fileName, workingDirectory, scopedEnvironment);
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(psi, cancellationToken, standardOutputReceived, standardErrorReceived);
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

    private static ProcessStartInfo CreateBaseProcess(string fileName, string workingDirectory, IReadOnlyDictionary<string, string?>? scopedEnvironment)
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

    public static IReadOnlyDictionary<string, string> BuildSafeEnvironment(IReadOnlyDictionary<string, string?>? scopedEnvironment)
    {
        var safeNames = new[]
        {
            "PATH", "HOME", "USER", "USERPROFILE", "TMPDIR", "TEMP", "TMP", "CI", "GITHUB_ACTIONS",
            "GITHUB_WORKSPACE", "RUNNER_TEMP", "DOTNET_ROOT", "DOTNET_CLI_TELEMETRY_OPTOUT"
        };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in safeNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                result[name] = value;
            }
        }

        result["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

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
        Action<string>? standardErrorReceived)
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
                standardOutputReceived?.Invoke(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
                standardErrorReceived?.Invoke(args.Data);
            }
        };

        logger.Info($"Starting command '{psi.FileName}'.");
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start command '{psi.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
