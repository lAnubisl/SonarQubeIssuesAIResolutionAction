namespace SonarCopilotFix.SonarQube;

public interface ISonarQubeHttpClient : IDisposable
{
    Uri BaseAddress { get; }
    Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken);
}
