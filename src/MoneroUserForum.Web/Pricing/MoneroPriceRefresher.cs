using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoneroUserForum.Web.Pricing;

/// <summary>
/// Polls moneropricenow.com for the XMR price once a minute and caches it in
/// <see cref="MoneroPriceService"/>. moneropricenow.com serves that value from its own
/// 15-second cache, so this polling never overwhelms it. Failures are non-fatal — the
/// last good price keeps showing.
/// </summary>
public sealed class MoneroPriceRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const string Url = "https://moneropricenow.com/api/price/xmr-usdt.json";

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
            if (dto is { PriceUsdt: > 0 })
            {
                this.service.Set(dto.PriceUsdt, dto.AsOfUtc == default ? DateTime.UtcNow : dto.AsOfUtc);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "XMR price refresh from moneropricenow.com failed.");
        }
    }

    private sealed record PriceDto(
        [property: JsonPropertyName("priceUsdt")] decimal PriceUsdt,
        [property: JsonPropertyName("asOfUtc")] DateTime AsOfUtc,
        [property: JsonPropertyName("source")] string? Source);
}
