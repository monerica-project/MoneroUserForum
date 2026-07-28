using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace MoneroUserForum.Web.Support;

public class Sponsor
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("link")] public string Link { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("sponsorshipType")] public string SponsorshipType { get; set; } = string.Empty;
    [JsonPropertyName("expirationDate")] public DateTime? ExpirationDate { get; set; }
    [JsonPropertyName("reviewRating")] public double? ReviewRating { get; set; }
    [JsonPropertyName("reviewCount")] public int ReviewCount { get; set; }
}

/// <summary>
/// Pulls active sponsors from Monerica's public JSON feed (same feed the other
/// network sites use) and caches them. Rendered server-side as clearly-labeled
/// sponsor cards, so ads work without JavaScript. Fails soft to no ads.
/// </summary>
public class SponsorService
{
    private const string CacheKey = "muf.sponsors";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory httpFactory;
    private readonly IConfiguration cfg;
    private readonly IMemoryCache cache;
    private readonly ILogger<SponsorService> log;

    public SponsorService(IHttpClientFactory httpFactory, IConfiguration cfg, IMemoryCache cache, ILogger<SponsorService> log)
    {
        this.httpFactory = httpFactory;
        this.cfg = cfg;
        this.cache = cache;
        this.log = log;
    }

    /// <summary>Main-tier sponsors only, most time remaining first.</summary>
    public async Task<List<Sponsor>> GetAsync(int count = 5)
    {
        var all = await this.LoadAsync();
        return all
            .Where(s => s.SponsorshipType.Equals("MainSponsor", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.ExpirationDate ?? DateTime.MinValue)
            .ThenBy(s => s.Name)
            .Take(count)
            .ToList();
    }

    /// <summary>Every active sponsor, all tiers — for the full sponsors page.</summary>
    public async Task<List<Sponsor>> GetAllAsync()
    {
        var all = await this.LoadAsync();
        return all
            .OrderBy(s => TierRank(s.SponsorshipType))
            .ThenByDescending(s => s.ExpirationDate ?? DateTime.MinValue)
            .ThenBy(s => s.Name)
            .ToList();
    }

    private async Task<List<Sponsor>> LoadAsync()
    {
        if (this.cache.TryGetValue(CacheKey, out List<Sponsor>? cached) && cached is not null)
        {
            return cached;
        }

        var url = this.cfg["Sponsors:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return new List<Sponsor>();
        }

        var minutes = int.TryParse(this.cfg["Sponsors:CacheMinutes"], out var m) ? m : 15;

        try
        {
            var c = this.httpFactory.CreateClient("sponsors");
            c.Timeout = TimeSpan.FromSeconds(10);
            var json = await c.GetStringAsync(url);
            var list = JsonSerializer.Deserialize<List<Sponsor>>(json, JsonOpts) ?? new List<Sponsor>();

            var now = DateTime.UtcNow;
            list = list
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Link))
                .Where(s => s.ExpirationDate is null || s.ExpirationDate > now)
                .GroupBy(s => s.Link, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(s => TierRank(s.SponsorshipType)).First())
                .ToList();

            this.cache.Set(CacheKey, list, TimeSpan.FromMinutes(minutes));
            return list;
        }
        catch (Exception ex)
        {
            this.log.LogWarning(ex, "Failed to load sponsors feed");
            this.cache.Set(CacheKey, new List<Sponsor>(), TimeSpan.FromMinutes(2));
            return new List<Sponsor>();
        }
    }

    private static int TierRank(string type) => type switch
    {
        not null when type.Equals("MainSponsor", StringComparison.OrdinalIgnoreCase) => 0,
        not null when type.Equals("CategorySponsor", StringComparison.OrdinalIgnoreCase) => 1,
        not null when type.Equals("SubcategorySponsor", StringComparison.OrdinalIgnoreCase) => 2,
        _ => 3,
    };
}
