using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MoneroUserForum.Services.Storage;

/// <summary>
/// Stores images in a Bunny.net Storage Zone (HTTP PUT/DELETE) and serves them
/// through the configured pull-zone CDN URL.
/// </summary>
public class BunnyImageStorage : IImageStorage
{
    private readonly HttpClient http;
    private readonly BunnyStorageOptions options;
    private readonly ILogger<BunnyImageStorage> logger;

    public BunnyImageStorage(HttpClient http, IOptions<BunnyStorageOptions> options, ILogger<BunnyImageStorage> logger)
    {
        this.http = http;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<string> SaveAsync(Stream content, string folder, string fileExtension, CancellationToken ct = default)
    {
        var key = $"{folder.Trim('/')}/{Guid.NewGuid():N}{NormalizeExt(fileExtension)}";
        var url = $"https://{this.options.StorageHost}/{this.options.StorageZoneName}/{key}";

        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.TryAddWithoutValidation("AccessKey", this.options.AccessKey);
        req.Content = new StreamContent(content);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var resp = await this.http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = $"Bunny upload failed ({(int)resp.StatusCode}) for {key}";
            this.logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        return key;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        var url = $"https://{this.options.StorageHost}/{this.options.StorageZoneName}/{storageKey.TrimStart('/')}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.TryAddWithoutValidation("AccessKey", this.options.AccessKey);
        using var resp = await this.http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            this.logger.LogWarning("Bunny delete failed ({Code}) for {Key}", (int)resp.StatusCode, storageKey);
        }
    }

    public string PublicUrl(string storageKey)
        => string.IsNullOrWhiteSpace(storageKey)
            ? string.Empty
            : $"{this.options.CdnBaseUrl.TrimEnd('/')}/{storageKey.TrimStart('/')}";

    private static string NormalizeExt(string ext)
    {
        ext = ext.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? string.Empty : "." + ext;
    }
}
