using System.Net.Http.Headers;
using Tinkwell.Firmlets.Registry.Client;

namespace Tinkwell.Cli.Commands.Hub;

internal sealed class RegistryClientFactory : IDisposable
{
    private readonly HttpClient _http;
    public FirmletRegistryApiClient Client { get; }
    public HttpClient GetHttpClient() => _http;

    public RegistryClientFactory(string baseUrl, string? apiKey)
    {
        var handler = new RetryAfterHandler { InnerHandler = new HttpClientHandler() };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Client = new FirmletRegistryApiClient(_http);
    }

    public void Dispose() => _http.Dispose();
}
