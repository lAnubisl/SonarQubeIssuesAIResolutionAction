using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler, ISonarQubeHttpClient
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public Uri BaseAddress { get; } = new("https://sonar.example");

    public Task<HttpResponseMessage> GetAsync(
        string requestUri,
        CancellationToken cancellationToken) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, requestUri)),
            cancellationToken);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
