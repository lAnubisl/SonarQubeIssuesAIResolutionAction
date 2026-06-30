namespace SonarCopilotFix.Infrastructure;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        CancellationToken cancellationToken = default,
        Action<string>? standardOutputReceived = null,
        Action<string>? standardErrorReceived = null,
        bool logCommandDetails = false);
}
