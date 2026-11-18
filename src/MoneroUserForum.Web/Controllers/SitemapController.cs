using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Services.Email;
using MoneroUserForum.Web.Support;

namespace MoneroUserForum.Web.Controllers;

public class SitemapController : Controller
{
    private readonly AppDbContext db;
    private readonly EmailOptions siteOptions;

    public SitemapController(AppDbContext db, IOptions<EmailOptions> siteOptions)
    {
        this.db = db;
        this.siteOptions = siteOptions.Value;
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Index()
    {
        // Configured site URL (or the configured onion host), never the raw Host header.
        var host = SiteLinks.BaseUrl(this.Request, this.siteOptions);

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        void Add(string path, DateTime? lastMod = null)
        {
            sb.Append("<url><loc>").Append(System.Security.SecurityElement.Escape(host + path)).Append("</loc>");
            if (lastMod is DateTime d)
            {
                sb.Append("<lastmod>").Append(d.ToString("yyyy-MM-dd")).Append("</lastmod>");
            }

            sb.Append("</url>");
        }

        Add("/");
        Add("/thread/categories");
        Add("/about");
        Add("/network");
        Add("/sponsors");
        foreach (var c in Enum.GetValues<ForumCategory>().Where(x => x != ForumCategory.Unknown))
        {
            Add($"/thread/category/{c.ToString().ToLowerInvariant()}");
        }

        var threads = await this.db.ForumThreads
            // Same rule as the public thread lists: no censored threads and none by unapproved members.
            .Where(ThreadVisibility.IsPublic)
            .OrderByDescending(t => t.LastPostAtUtc)
            .Select(t => new { t.Slug, t.LastPostAtUtc })
            .Take(5000)
            .ToListAsync();

        foreach (var t in threads)
        {
            Add($"/thread/{t.Slug}", t.LastPostAtUtc);
        }

        sb.Append("</urlset>");
        return this.Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
