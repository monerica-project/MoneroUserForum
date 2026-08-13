using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoneroUserForum.Web.Pricing;

/// <summary>
/// Polls the MoneroPriceNow public CDN API for the XMR price once a minute and caches it in
/// <see cref="MoneroPriceService"/>. api.moneropricenow.com is served from Bunny's edge (the file
/// is republished every ~30s), so this polling hits the CDN, never the web origin. Failures are
/// non-fatal — the last good price keeps showing.
/// </summary>
public sealed class MoneroPriceRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const string Url = "https://api.moneropricenow.com/xmr-usdt.json";

    private readonly IHttpClientFactory httpFactory;
    private readonly MoneroPriceService service;
    private readonly ILogger<MoneroPriceRefresher> logger;

    public MoneroPriceRefresher(IHttpClientFactory httpFactory, MoneroPriceService service, ILogger<MoneroPriceRefresher> logger)
    {
        this.httpFactory = httpFactory;
        this.service = service;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fetch once up front, then every minute.
        await this.RefreshAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await this.RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var client = this.httpFactory.CreateClient("moneroprice");
            var dto = await client.GetFromJsonAsync<PriceDto>(Url, ct);
            if (dto is { Price: > 0 })
            {
                this.service.Set(dto.Price, dto.PublishedUtc == default ? DateTime.UtcNow : dto.PublishedUtc);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "XMR price refresh from api.moneropricenow.com failed.");
        }
    }

    private sealed record PriceDto(
        [property: JsonPropertyName("price")] decimal Price,
        [property: JsonPropertyName("published_utc")] DateTime PublishedUtc,
        [property: JsonPropertyName("pair")] string? Pair,
        [property: JsonPropertyName("source")] string? Source);
}
