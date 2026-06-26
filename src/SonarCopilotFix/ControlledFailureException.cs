namespace SonarCopilotFix;

public sealed class ControlledFailureException(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
