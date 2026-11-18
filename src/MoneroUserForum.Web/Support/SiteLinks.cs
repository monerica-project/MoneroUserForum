using MoneroUserForum.Services.Email;

namespace MoneroUserForum.Web.Support;

/// <summary>
/// Absolute links the site hands out (reset emails, admin mail, sitemap, canonical/og:url).
/// They are never built from the request's Host header: an attacker can send any Host (any
/// .onion name gets past the onion nginx), and a reset email whose link points at the attacker's
/// host gives them the reset token. Only the configured site URL, or the configured onion host
/// when the request really arrived on it, is used.
/// </summary>
public static class SiteLinks
{
    public static string BaseUrl(HttpRequest request, EmailOptions options)
    {
        var onion = (options.OnionHost ?? string.Empty).Trim().TrimEnd('/');
        if (onion.Length > 0 && string.Equals(request.Host.Host, onion, StringComparison.OrdinalIgnoreCase))
        {
            // Onion services are plain http (Tor provides the encryption).
            return "http://" + onion.ToLowerInvariant();
        }

        return ClearnetBaseUrl(options);
    }

    /// <summary>The configured public site URL, without a trailing slash.</summary>
    public static string ClearnetBaseUrl(EmailOptions options)
    {
        var site = (options.SiteUrl ?? string.Empty).Trim().TrimEnd('/');
        return site.Length > 0 ? site : "https://monerouserforum.com";
    }
}
