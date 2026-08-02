namespace SonarCopilotFix.Interfaces;

public interface ICopilotCliRunner
{
    Task<string> RunAsync(string prompt, CancellationToken cancellationToken);
}
