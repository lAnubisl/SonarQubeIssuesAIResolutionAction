namespace SonarCopilotFix.Interfaces;

public interface IApplication
{
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}
