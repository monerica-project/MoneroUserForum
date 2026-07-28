using System.ComponentModel;
using System.Reflection;

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

    public static string TimeAgo(DateTime utc)
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
