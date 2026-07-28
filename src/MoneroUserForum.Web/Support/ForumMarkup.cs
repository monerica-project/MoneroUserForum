using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;

namespace MoneroUserForum.Web.Support;

/// <summary>
/// Renders a post's markdown body to safe HTML: markdown → HTML (Markdig, with
/// raw HTML disabled and bare URLs auto-linked) → sanitized allowlist (Ganss).
/// Links get rel="nofollow noopener ugc" and open in a new tab. This is what lets
/// members add hyperlinks (and the toolbar produce them) without any XSS risk.
/// </summary>
public static class ForumMarkup
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = Markdown.ToHtml(markdown, Pipeline);
        return Sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();

        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "strong", "em", "b", "i", "u", "s", "code", "pre", "blockquote", "ul", "ol", "li", "a", "h3", "h4", "hr", "span" })
        {
            s.AllowedTags.Add(t);
        }

        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");

        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");

        // Harden every surviving link.
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement el && el.NodeName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("rel", "nofollow noopener ugc");
                el.SetAttribute("target", "_blank");
            }
        };

        return s;
    }
}
