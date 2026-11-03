using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Html;

namespace MoneroUserForum.Web.Support;

/// <summary>Small view helpers: enum labels and relative timestamps.</summary>
public static class ForumDisplay
{
    public static string Describe(Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? value.ToString();
    }

    /// <summary>
    /// Returns the URL only if it's an absolute http/https address — otherwise null.
    /// This blocks javascript:, data:, and other scheme-based XSS in link hrefs.
    /// </summary>
    public static string? SafeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)
               && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? u.AbsoluteUri
            : null;
    }

    /// <summary>
    /// A relative time ("3h ago") wrapped in a &lt;time&gt; tag that carries the exact UTC instant.
    /// Without JavaScript the server's text is shown; with it, wwwroot/js/local-time.js recomputes it
    /// from the reader's own clock and shows their local date/time on hover.
    /// </summary>
    public static IHtmlContent TimeAgo(DateTime utc) =>
        new HtmlString(
            $"<time class=\"ago\" datetime=\"{IsoUtc(utc)}\" title=\"{WebUtility.HtmlEncode(utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} UTC\">"
            + $"{WebUtility.HtmlEncode(TimeAgoText(utc))}</time>");

    /// <summary>An exact time stamp in UTC (yyyy-MM-ddTHH:mm:ssZ), for &lt;time datetime&gt;.</summary>
    public static string IsoUtc(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Plain-text relative time, computed on the server in UTC.</summary>
    public static string TimeAgoText(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 60)
        {
            return "just now";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes}m ago";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours}h ago";
        }

        if (span.TotalDays < 30)
        {
            return $"{(int)span.TotalDays}d ago";
        }

        if (span.TotalDays < 365)
        {
            return $"{(int)(span.TotalDays / 30)}mo ago";
        }

        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}
