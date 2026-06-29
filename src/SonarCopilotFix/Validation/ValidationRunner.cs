using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Validation;

public sealed class ValidationRunner(CommandRunner commandRunner, string workspace)
{
    public Task<CommandResult> RunAsync(string command, CancellationToken cancellationToken)
    {
        return commandRunner.RunShellAsync(command, workspace, cancellationToken: cancellationToken);
    }

    public static string BuildFailureMessage(CommandResult result)
    {
        var message = $"Validation command failed with exit code {result.ExitCode}. Leaving changes in the workspace for inspection.";
        return string.IsNullOrWhiteSpace(result.Summary)
            ? message
            : $"{message}{Environment.NewLine}Validation output:{Environment.NewLine}{result.Summary}";
    }
}
