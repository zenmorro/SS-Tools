using System.Net;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

/// <summary>
/// Minimal HttpClient factory that honors the user's proxy setting. Kept as an
/// interface so services can be unit-tested with a fake handler.
/// </summary>
public interface IHttpClientFactoryLite
{
    HttpClient Create();
}

public sealed class HttpClientFactoryLite : IHttpClientFactoryLite
{
    private readonly ISettingsService _settings;

    public HttpClientFactoryLite(ISettingsService settings) => _settings = settings;

    public HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        var proxyUrl = _settings.Current.ProxyUrl;
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl) { UseDefaultCredentials = true };
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ForensicKit");
        return client;
    }
}
