using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Validation;

public sealed class ValidationRunner(CommandRunner commandRunner, string workspace)
{
    public Task<CommandResult> RunAsync(string command, CancellationToken cancellationToken)
    {
        return commandRunner.RunShellAsync(command, workspace, cancellationToken: cancellationToken);
    }
}
