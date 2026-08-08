using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Controllers;

public class SitemapController : Controller
{
    private readonly AppDbContext db;

    public SitemapController(AppDbContext db)
    {
        this.db = db;
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Index()
    {
        var host = $"{this.Request.Scheme}://{this.Request.Host}";

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
            .Where(t => !t.IsDeleted && !t.Author!.IsDeleted)
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
