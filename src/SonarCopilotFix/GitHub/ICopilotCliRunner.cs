namespace SonarCopilotFix.GitHub;

public interface ICopilotCliRunner
{
    Task<string> RunAsync(string prompt, CancellationToken cancellationToken);
}
