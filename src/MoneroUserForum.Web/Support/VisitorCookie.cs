namespace MoneroUserForum.Web.Support;

/// <summary>
/// The random per-browser key behind "unique views" (never an IP). A view is counted only when the
/// browser sent back a key it was given earlier: a script that never keeps cookies would otherwise
/// add a new row to ThreadViews on every request and inflate the counts without limit.
/// </summary>
public static class VisitorCookie
{
    public const string Name = "muf.vid";

    /// <summary>The key the browser sent, or null when it is missing or not one we could have issued.</summary>
    public static string? ReadExisting(string? cookieValue)
    {
        if (string.IsNullOrEmpty(cookieValue) || cookieValue.Length != 32)
        {
            return null;
        }

        // Issued keys are Guid "N" format: 32 lowercase hex characters.
        foreach (var c in cookieValue)
        {
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return null;
            }
        }

        return cookieValue;
    }

    public static string NewKey() => Guid.NewGuid().ToString("N");
}
