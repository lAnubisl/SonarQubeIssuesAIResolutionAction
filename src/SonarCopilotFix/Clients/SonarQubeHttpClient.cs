using System.Net.Http.Headers;

namespace SonarCopilotFix.Clients;

public sealed class SonarQubeHttpClient : ISonarQubeHttpClient
{
    private readonly HttpClient _httpClient = new();

    public SonarQubeHttpClient(IConfigurationHelper configurationHelper)
    {
        _httpClient.BaseAddress = configurationHelper.SonarHostUri;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configurationHelper.SonarToken);
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
