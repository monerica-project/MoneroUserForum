using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Services.Moderation;

namespace MoneroUserForum.Web.Moderation;

/// <summary>
/// Every few minutes, checks public posts (new, edited, or never checked) with the AI safety
/// check and records the result per post. It never hides or deletes anything: flagged posts wait
/// on /admin/moderation for an admin. Private messages are end-to-end encrypted and never checked.
/// Without an Anthropic API key in the environment it does nothing.
/// </summary>
public sealed class PostModerationScanner : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly IHttpClientFactory httpFactory;
    private readonly ModerationOptions options;
    private readonly string apiKey;
    private readonly ILogger<PostModerationScanner> logger;

    public PostModerationScanner(IServiceScopeFactory scopes, IHttpClientFactory httpFactory, IConfiguration config, ILogger<PostModerationScanner> logger)
    {
        this.scopes = scopes;
        this.httpFactory = httpFactory;
        this.logger = logger;
        this.options = config.GetSection("Moderation").Get<ModerationOptions>() ?? new ModerationOptions();
        this.apiKey = config["Anthropic:ApiKey"] ?? string.Empty;
    }

    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"]);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.options.Enabled || string.IsNullOrWhiteSpace(this.apiKey))
        {
            this.logger.LogInformation("Post moderation scanner off (disabled or no Anthropic:ApiKey).");
            return;
        }

        // Let the app finish starting (migrations, warm-up) first.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, this.options.IntervalMinutes)));
        do
        {
            try
            {
                await this.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                this.logger.LogError(ex, "Post moderation run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = this.scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var today = DateTime.UtcNow.Date;
        var checkedToday = await db.PostModerations.CountAsync(m => m.CheckedAtUtc >= today && m.Model != null, ct);
        var budget = Math.Min(this.options.PerRunCap, this.options.DailyCap - checkedToday);
        if (budget <= 0)
        {
            return 0;
        }

        // Candidates: live public posts. Compare hashes in memory (cheap at forum scale).
        var posts = await db.Posts.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsCensored && !p.Thread!.IsDeleted && !p.Thread.IsCensored)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Body, p.IsOpeningPost, Title = p.Thread!.Title })
            .ToListAsync(ct);
        var existing = await db.PostModerations.AsNoTracking()
            .Select(m => new { m.PostId, m.BodyHash, m.State })
            .ToDictionaryAsync(m => m.PostId, ct);

        var todo = posts
            .Select(p => new ModerationItem(p.Id, p.IsOpeningPost ? p.Title : null, p.Body ?? string.Empty))
            .Where(i => !existing.TryGetValue(i.PostId, out var m)
                        || m.State == PostModerationState.Error
                        || m.BodyHash != ModerationHash.Of(i.ThreadTitle, i.Body))
            .Take(budget)
            .ToList();
        if (todo.Count == 0)
        {
            return 0;
        }

        var client = new ClaudeModerationClient(this.httpFactory.CreateClient("anthropic"), this.options, this.apiKey);
        int flagged = 0;
        foreach (var batch in todo.Chunk(Math.Max(1, this.options.BatchSize)))
        {
            var result = await client.CheckAsync(batch, ct);

            // A declined batch is re-checked one post at a time, so one bad post doesn't hide the rest.
            if (result.Error != null && result.Error.Contains("refusal", StringComparison.Ordinal) && batch.Length > 1)
            {
                foreach (var single in batch)
                {
                    flagged += await this.SaveAsync(db, new[] { single }, await client.CheckAsync(new[] { single }, ct), ct);
                }

                continue;
            }

            flagged += await this.SaveAsync(db, batch, result, ct);
        }

        this.logger.LogInformation("Post moderation: checked {Count} post(s), {Flagged} flagged.", todo.Count, flagged);
        return todo.Count;
    }

    private async Task<int> SaveAsync(AppDbContext db, IReadOnlyList<ModerationItem> batch, ModerationBatchResult result, CancellationToken ct)
    {
        var ids = batch.Select(b => b.PostId).ToList();
        var rows = await db.PostModerations.Where(m => ids.Contains(m.PostId)).ToDictionaryAsync(m => m.PostId, ct);
        var now = DateTime.UtcNow;
        var tokensIn = result.TokensIn / Math.Max(1, batch.Count);
        var tokensOut = result.TokensOut / Math.Max(1, batch.Count);
        int flagged = 0;

        foreach (var item in batch)
        {
            if (!rows.TryGetValue(item.PostId, out var row))
            {
                row = new PostModeration { PostId = item.PostId };
                db.PostModerations.Add(row);
            }

            row.BodyHash = ModerationHash.Of(item.ThreadTitle, item.Body);
            row.CheckedAtUtc = now;
            row.Model = result.Model;
            row.TokensIn = tokensIn;
            row.TokensOut = tokensOut;
            row.AdminActionAtUtc = null;
            row.AdminActionBy = null;

            var verdict = result.Verdicts.FirstOrDefault(v => v.PostId == item.PostId);
            if (verdict == null && result.Error != null && result.Error.Contains("refusal", StringComparison.Ordinal))
            {
                // The model would not even look at this one post: that alone is worth an admin's look.
                row.State = PostModerationState.Flagged;
                row.Severity = "medium";
                row.Categories = "other";
                row.Reason = "The automatic check declined to review this post. Please look at it yourself.";
                row.Error = null;
                flagged++;
                continue;
            }

            if (verdict == null)
            {
                row.State = PostModerationState.Error;
                row.Error = result.Error ?? "No result returned for this post";
                row.Model = null; // failed checks don't count toward the daily cap
                continue;
            }

            row.State = verdict.Flag ? PostModerationState.Flagged : PostModerationState.Clean;
            row.Severity = verdict.Severity;
            row.Categories = verdict.Categories.Count == 0 ? null : string.Join(",", verdict.Categories);
            row.Reason = string.IsNullOrWhiteSpace(verdict.Reason) ? null : verdict.Reason;
            row.Error = null;
            if (verdict.Flag)
            {
                flagged++;
            }
        }

        await db.SaveChangesAsync(ct);
        return flagged;
    }
}
