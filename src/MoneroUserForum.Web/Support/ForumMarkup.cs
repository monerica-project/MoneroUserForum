using System.Text.RegularExpressions;
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
        html = Sanitizer.Sanitize(html);
        html = HighlightMentions(html);
        return EmbedYouTube(html);
    }

    // Splits the (already-sanitized) HTML into tags vs. text runs so the mention regex only ever
    // touches visible text — never attribute values inside a tag — and so the <span> we inject
    // can't produce broken markup. Handles match MentionParser (@ + 4-20 of [A-Za-z0-9_-]).
    private static readonly Regex TagOrTextRegex = new(
        @"<[^>]+>|[^<]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex MentionRegex = new(
        @"(?<![\w@])@([A-Za-z0-9_-]{4,20})", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    /// <summary>Wraps @username mentions in <c>&lt;span class="mention"&gt;</c> so they stand out,
    /// matching who actually gets a mention notification. Runs after sanitization on trusted markup.</summary>
    private static string HighlightMentions(string html)
    {
        return TagOrTextRegex.Replace(html, seg =>
        {
            var s = seg.Value;
            if (s.Length > 0 && s[0] == '<')
            {
                return s; // a tag — never rewrite inside it
            }

            return MentionRegex.Replace(s, m => $"<span class=\"mention\">@{m.Groups[1].Value}</span>");
        });
    }

    // A paragraph whose only content is a single YouTube link → turn it into a
    // click-to-play facade (see EmbedYouTube). Runs AFTER sanitization on our own
    // trusted markup, and the video id is validated to [A-Za-z0-9_-]{11}, so there
    // is no XSS surface.
    private static readonly Regex YoutubeLinkParaRegex = new(
        @"<p>\s*<a\s+href=""(?<url>https?://[^""]*(?:youtube\.com|youtu\.be)[^""]*)""[^>]*>[^<]*</a>\s*</p>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex YoutubeIdRegex = new(
        @"(?:youtu\.be/|youtube\.com/(?:watch\?(?:[^""]*&)?v=|embed/|shorts/|v/|live/))(?<id>[A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static string EmbedYouTube(string html)
    {
        return YoutubeLinkParaRegex.Replace(html, m =>
        {
            var idMatch = YoutubeIdRegex.Match(m.Groups["url"].Value);
            if (!idMatch.Success)
            {
                return m.Value; // not a recognizable video URL — leave the link as-is
            }

            var id = idMatch.Groups["id"].Value; // safe: [A-Za-z0-9_-]{11}
            return
                $"<div class=\"yt-embed\" data-ytid=\"{id}\">" +
                    "<button type=\"button\" class=\"yt-play\" aria-label=\"Play video\">" +
                        // Real video thumbnail. If it can't load (Tor/VPN blocks Google),
                        // yt-embed.js hides it and the dark card + play icon still show.
                        $"<img class=\"yt-thumb\" src=\"https://i.ytimg.com/vi/{id}/hqdefault.jpg\" alt=\"\" loading=\"lazy\" />" +
                        "<span class=\"yt-play-icon\" aria-hidden=\"true\">&#9654;</span>" +
                    "</button>" +
                    $"<a class=\"yt-fallback\" href=\"https://www.youtube.com/watch?v={id}\" target=\"_blank\" rel=\"nofollow noopener ugc\">Can&#39;t watch here? Open on YouTube &#8599;</a>" +
                "</div>";
        });
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
