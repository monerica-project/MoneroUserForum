using System.Text.RegularExpressions;

namespace MoneroUserForum.Services.Text;

/// <summary>Extracts @username mentions from post bodies.</summary>
public static partial class MentionParser
{
    [GeneratedRegex(@"(?<![\w@])@([a-zA-Z0-9_-]{4,20})", RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    /// <summary>
    /// Most members one post can notify. Every mention can send an email, so without a cap one post
    /// could mail a long list of members at once.
    /// </summary>
    public const int MaxMentionsPerPost = 5;

    /// <summary>Returns the distinct lowercased handles mentioned in <paramref name="body"/> (first <see cref="MaxMentionsPerPost"/> only).</summary>
    public static IReadOnlyCollection<string> Extract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in MentionRegex().Matches(body))
        {
            set.Add(m.Groups[1].Value.ToLowerInvariant());
            if (set.Count >= MaxMentionsPerPost)
            {
                break;
            }
        }

        return set;
    }
}
