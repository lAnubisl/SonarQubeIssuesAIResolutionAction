using System.Net.Http.Headers;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.SonarQube;

public sealed class SonarQubeHttpClient : ISonarQubeHttpClient
{
    private readonly HttpClient _httpClient = new();

    public SonarQubeHttpClient(IConfigurationHelper configurationHelper)
    {
        _httpClient.BaseAddress = configurationHelper.GetSonarHostUri();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configurationHelper.GetSonarToken());
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Uri BaseAddress => _httpClient.BaseAddress!;

    public Task<HttpResponseMessage> GetAsync(
        string requestUri,
        CancellationToken cancellationToken) =>
        _httpClient.GetAsync(requestUri, cancellationToken);

    public void Dispose() => _httpClient.Dispose();
}
