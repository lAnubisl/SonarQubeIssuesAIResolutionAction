
namespace SonarCopilotFix.Interfaces;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        Action<string>? standardOutputReceived = null,
        Action<string>? standardErrorReceived = null,
        CancellationToken cancellationToken = default);
}
