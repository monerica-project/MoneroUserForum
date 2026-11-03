using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg.Sig;

namespace MoneroUserForum.Services.Pgp;

/// <summary>What we learned from a member's armored PGP public key.</summary>
public sealed record PgpKeyInfo(string Fingerprint, IReadOnlyCollection<long> KeyIds);

/// <summary>Result of checking that a private message body is a PGP message encrypted to the recipient.</summary>
public enum PgpMessageCheck
{
    Ok,

    /// <summary>Not an armored "BEGIN PGP MESSAGE" block (plain text, or text around the block).</summary>
    NotPgpMessage,

    /// <summary>Armored, but not public-key encrypted data (e.g. signed only, or password-encrypted).</summary>
    NotEncrypted,

    /// <summary>Encrypted, but to none of the recipient's keys.</summary>
    WrongRecipient,
}

/// <summary>
/// OpenPGP helpers for member keys and private messages. The server never decrypts anything:
/// it only checks keys, encrypts the one-time verification code, and inspects message headers
/// (which key ids a message is encrypted to).
/// </summary>
public static class PgpTools
{
    public const string MessageHeader = "-----BEGIN PGP MESSAGE-----";
    public const string MessageFooter = "-----END PGP MESSAGE-----";

    /// <summary>
    /// Parses an armored public key. Returns null (with a member-facing reason) when it isn't a
    /// public key block or has no key that can receive encrypted messages.
    /// </summary>
    public static PgpKeyInfo? ReadPublicKey(string? armored, out string? error)
    {
        error = null;
        if (armored is not null && armored.Contains("BEGIN PGP PRIVATE KEY BLOCK", StringComparison.Ordinal))
        {
            error = "That looks like a PRIVATE key. Never share it — paste your PUBLIC key instead.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(armored) || !armored.Contains("BEGIN PGP PUBLIC KEY BLOCK", StringComparison.Ordinal))
        {
            error = "Paste your whole PGP public key, starting with -----BEGIN PGP PUBLIC KEY BLOCK-----.";
            return null;
        }

        try
        {
            var ring = FirstRing(armored);
            if (ring is null)
            {
                error = "No PGP public key was found in what you pasted.";
                return null;
            }

            if (SelectEncryptionKey(ring) is null)
            {
                error = "This key can't receive encrypted messages (no valid, unexpired encryption key). Paste a key with an encryption subkey.";
                return null;
            }

            var ids = ring.GetPublicKeys().Cast<PgpPublicKey>().Select(k => k.KeyId).ToArray();
            return new PgpKeyInfo(Hex(ring.GetPublicKey().GetFingerprint()), ids);
        }
        catch
        {
            error = "That PGP public key couldn't be read. Make sure you pasted the full armored block.";
            return null;
        }
    }

    /// <summary>Encrypts text to the key's encryption key (armored, AES-256, with integrity protection).</summary>
    public static string EncryptTo(string armoredPublicKey, string text)
    {
        var ring = FirstRing(armoredPublicKey) ?? throw new InvalidOperationException("No public key.");
        var encKey = SelectEncryptionKey(ring) ?? throw new InvalidOperationException("No encryption-capable key.");
        var data = Encoding.UTF8.GetBytes(text);

        using var outMs = new MemoryStream();
        using (var armoredOut = new ArmoredOutputStream(outMs))
        {
            armoredOut.SetHeader("Version", null);
            var encGen = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, true);
            encGen.AddMethod(encKey);
            using var encOut = encGen.Open(armoredOut, new byte[1 << 16]);
            var litGen = new PgpLiteralDataGenerator();
            using var litOut = litGen.Open(encOut, PgpLiteralData.Utf8, "", data.Length, DateTime.UtcNow);
            litOut.Write(data, 0, data.Length);
        }

        return Encoding.ASCII.GetString(outMs.ToArray());
    }

    /// <summary>
    /// The body must be exactly one armored PGP message (nothing outside it), public-key encrypted
    /// to at least one of the recipient's keys. A hidden recipient (key id 0, gpg --throw-keys) is accepted.
    /// </summary>
    public static PgpMessageCheck CheckMessage(string? body, IReadOnlyCollection<long> recipientKeyIds)
    {
        var text = (body ?? string.Empty).Trim();
        if (!text.StartsWith(MessageHeader, StringComparison.Ordinal)
            || !text.EndsWith(MessageFooter, StringComparison.Ordinal)
            || text.IndexOf(MessageHeader, 1, StringComparison.Ordinal) >= 0)
        {
            return PgpMessageCheck.NotPgpMessage;
        }

        try
        {
            using var input = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.ASCII.GetBytes(text)));
            var factory = new PgpObjectFactory(input);
            var obj = factory.NextPgpObject();
            while (obj is PgpMarker)
            {
                obj = factory.NextPgpObject();
            }

            if (obj is not PgpEncryptedDataList list)
            {
                return PgpMessageCheck.NotEncrypted;
            }

            var pubKeyIds = list.GetEncryptedDataObjects().OfType<PgpPublicKeyEncryptedData>().Select(d => d.KeyId).ToList();
            if (pubKeyIds.Count == 0)
            {
                return PgpMessageCheck.NotEncrypted; // password-only (symmetric) encryption
            }

            return pubKeyIds.Any(id => id == 0 || recipientKeyIds.Contains(id))
                ? PgpMessageCheck.Ok
                : PgpMessageCheck.WrongRecipient;
        }
        catch
        {
            return PgpMessageCheck.NotPgpMessage;
        }
    }

    /// <summary>"ABCD 1234 …" grouping for display.</summary>
    public static string FormatFingerprint(string? fp)
    {
        if (string.IsNullOrEmpty(fp))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < fp.Length; i += 4)
        {
            if (i > 0)
            {
                sb.Append(i == fp.Length / 2 ? "  " : " ");
            }

            sb.Append(fp, i, Math.Min(4, fp.Length - i));
        }

        return sb.ToString();
    }

    /// <summary>A random verification code (12 chars, ~60 bits) that's easy to read and type.</summary>
    public static string NewChallengeCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }

    public static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeCode(code))));

    /// <summary>Uppercases and strips spaces/dashes so "abcd-efgh …" still matches.</summary>
    public static string NormalizeCode(string? code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool CodeMatches(string? typed, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        var a = Encoding.ASCII.GetBytes(HashCode(typed));
        var b = Encoding.ASCII.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Only the first key ring is ever used, so a pasted block can't mix two people's keys.</summary>
    private static PgpPublicKeyRing? FirstRing(string armored)
    {
        using var keyIn = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.UTF8.GetBytes(armored)));
        var bundle = new PgpPublicKeyRingBundle(keyIn);
        return bundle.GetKeyRings().Cast<PgpPublicKeyRing>().FirstOrDefault();
    }

    /// <summary>Newest valid encryption subkey (binding signature verified), else an encryption-capable primary.</summary>
    private static PgpPublicKey? SelectEncryptionKey(PgpPublicKeyRing ring)
    {
        var primary = ring.GetPublicKey();
        PgpPublicKey? bestSub = null;
        PgpPublicKey? bestPrimary = null;

        foreach (PgpPublicKey k in ring.GetPublicKeys())
        {
            if (!k.IsEncryptionKey || k.HasRevocation())
            {
                continue;
            }

            var valid = k.GetValidSeconds();
            if (valid > 0 && k.CreationTime.AddSeconds(valid) < DateTime.UtcNow)
            {
                continue;
            }

            if (k.IsMasterKey)
            {
                var flags = AdvertisedFlags(primary, k);
                if (flags is null || (flags.Value & (KeyFlags.EncryptComms | KeyFlags.EncryptStorage)) != 0)
                {
                    bestPrimary = k;
                }
            }
            else if (HasValidEncryptionBinding(primary, k) && (bestSub is null || k.CreationTime > bestSub.CreationTime))
            {
                bestSub = k;
            }
        }

        return bestSub ?? bestPrimary;
    }

    private static bool HasValidEncryptionBinding(PgpPublicKey primary, PgpPublicKey subkey)
    {
        foreach (PgpSignature sig in subkey.GetSignatures())
        {
            if (sig.KeyId != primary.KeyId || sig.SignatureType != PgpSignature.SubkeyBinding)
            {
                continue;
            }

            var hashed = sig.GetHashedSubPackets();
            if (hashed is not null && hashed.HasSubpacket(SignatureSubpacketTag.KeyFlags)
                && (hashed.GetKeyFlags() & (KeyFlags.EncryptComms | KeyFlags.EncryptStorage)) == 0)
            {
                continue;
            }

            try
            {
                sig.InitVerify(primary);
                if (sig.VerifyCertification(primary, subkey))
                {
                    return true;
                }
            }
            catch
            {
                // malformed signature: try the next one
            }
        }

        return false;
    }

    private static int? AdvertisedFlags(PgpPublicKey primary, PgpPublicKey key)
    {
        int? flags = null;
        foreach (PgpSignature sig in key.GetSignatures())
        {
            var hashed = sig.GetHashedSubPackets();
            if (sig.KeyId == primary.KeyId && hashed is not null && hashed.HasSubpacket(SignatureSubpacketTag.KeyFlags))
            {
                flags = (flags ?? 0) | hashed.GetKeyFlags();
            }
        }

        return flags;
    }

    private static string Hex(byte[] data) => Convert.ToHexString(data);
}
