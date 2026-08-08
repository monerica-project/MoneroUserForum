using System.Text.RegularExpressions;

namespace MoneroUserForum.Web.Support;

/// <summary>Cheap "does this text contain a link?" check, used to flag received DMs that carry a
/// URL so the recipient is nudged to double-check it. Deliberately generous — a false positive
/// just shows an extra "check the link" note, which is harmless.</summary>
public static partial class LinkHeuristics
{
    [GeneratedRegex(
        @"(https?://|www\.|\b[a-z0-9-]+\.(?:com|net|org|io|co|xyz|onion|to|cc|me|info|app|dev|gg|link|top|site|xmr|ru|eu|de|uk))([/?#]|\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    public static bool HasLink(string? text) =>
        !string.IsNullOrEmpty(text) && LinkRegex().IsMatch(text);
}
