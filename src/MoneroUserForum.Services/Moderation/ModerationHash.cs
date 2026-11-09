using System.Security.Cryptography;
using System.Text;

namespace MoneroUserForum.Services.Moderation;

public static class ModerationHash
{
    /// <summary>Hash of exactly what gets checked, so an edited post (or thread title) is checked again.</summary>
    public static string Of(string? threadTitle, string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((threadTitle ?? string.Empty) + "\n\u0001\n" + body))).ToLowerInvariant();
}
