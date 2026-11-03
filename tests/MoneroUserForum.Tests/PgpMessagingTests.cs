using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Pgp;
using MoneroUserForum.Web.Controllers;
using Xunit;

namespace MoneroUserForum.Tests;

/// <summary>
/// Server-side PGP rules for private messages and profile keys. Fixtures are real gpg output
/// (Alice = ed25519 + cv25519 subkey, Bob = RSA 3072 + RSA subkey, Signer = sign-only).
/// </summary>
public class PgpMessagingTests
{
    private static string Fx(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static AppUser Member(string name, string? keyFile, bool verified = true)
    {
        var key = keyFile is null ? null : Fx(keyFile);
        return new AppUser
        {
            DisplayName = name,
            PgpPublicKey = key,
            PgpFingerprint = key is null ? null : PgpTools.ReadPublicKey(key, out _)?.Fingerprint,
            PgpVerifiedAtUtc = verified ? DateTime.UtcNow : null,
        };
    }

    [Fact]
    public void Reads_ecc_and_rsa_keys_with_primary_fingerprint()
    {
        var alice = PgpTools.ReadPublicKey(Fx("alice.pub.asc"), out var err);
        Assert.Null(err);
        Assert.Equal("85BAD6985976856BD3D607C6668441B13CDF6179", alice!.Fingerprint);
        Assert.Equal(2, alice.KeyIds.Count);
        Assert.NotNull(PgpTools.ReadPublicKey(Fx("bob.pub.asc"), out _));
    }

    [Theory]
    [InlineData("signonly.pub.asc")] // no encryption key
    [InlineData("to-alice.asc")]     // a message, not a key
    public void Rejects_keys_that_cannot_receive_messages(string file)
    {
        Assert.Null(PgpTools.ReadPublicKey(Fx(file), out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Rejects_private_key_and_garbage()
    {
        Assert.Null(PgpTools.ReadPublicKey("-----BEGIN PGP PRIVATE KEY BLOCK-----\nabc\n-----END PGP PRIVATE KEY BLOCK-----", out var e1));
        Assert.Contains("PRIVATE", e1);
        Assert.Null(PgpTools.ReadPublicKey("-----BEGIN PGP PUBLIC KEY BLOCK-----\nnot base64 !!\n-----END PGP PUBLIC KEY BLOCK-----", out _));
        Assert.Null(PgpTools.ReadPublicKey("hello", out _));
    }

    [Theory]
    [InlineData("to-alice.asc")]
    [InlineData("to-alice-and-bob.asc")]
    [InlineData("hidden-recipient.asc")]
    public void Accepts_messages_encrypted_to_the_recipient(string file)
    {
        Assert.Null(MessagesController.CheckPgpBody(Fx(file), Member("alice", "alice.pub.asc")));
    }

    [Fact]
    public void Accepts_message_with_windows_newlines_and_surrounding_whitespace()
    {
        var body = "\r\n  " + Fx("to-alice.asc").Replace("\n", "\r\n") + "\r\n";
        Assert.Null(MessagesController.CheckPgpBody(body, Member("alice", "alice.pub.asc")));
    }

    [Fact]
    public void Rejects_message_encrypted_to_someone_else()
    {
        var err = MessagesController.CheckPgpBody(Fx("to-bob.asc"), Member("alice", "alice.pub.asc"));
        Assert.Contains("not to alice's key", err);
    }

    [Theory]
    [InlineData("symmetric.asc")]
    [InlineData("signed-only.asc")]
    public void Rejects_pgp_that_is_not_public_key_encrypted(string file)
    {
        Assert.Contains("isn't encrypted to a public key", MessagesController.CheckPgpBody(Fx(file), Member("alice", "alice.pub.asc")));
    }

    [Theory]
    [InlineData("hi alice, this is plain text")]
    [InlineData("")]
    [InlineData("-----BEGIN PGP MESSAGE-----\nnot really\n-----END PGP MESSAGE-----")]
    public void Rejects_plain_text_and_fake_blocks(string body)
    {
        Assert.NotNull(MessagesController.CheckPgpBody(body, Member("alice", "alice.pub.asc")));
    }

    [Fact]
    public void Rejects_clearsigned_text_and_plaintext_around_a_real_block()
    {
        var alice = Member("alice", "alice.pub.asc");
        Assert.NotNull(MessagesController.CheckPgpBody(Fx("clearsigned.asc"), alice));
        Assert.Contains("must be PGP encrypted", MessagesController.CheckPgpBody("secret plaintext\n" + Fx("to-alice.asc"), alice));
        Assert.NotNull(MessagesController.CheckPgpBody(Fx("to-alice.asc") + "\nP.S. plaintext", alice));
        Assert.NotNull(MessagesController.CheckPgpBody(Fx("to-alice.asc") + "\n" + Fx("to-alice.asc"), alice));
    }

    [Fact]
    public void Rejects_recipient_without_verified_key()
    {
        Assert.Contains("hasn't added a verified PGP key", MessagesController.CheckPgpBody(Fx("to-alice.asc"), Member("alice", "alice.pub.asc", verified: false)));
        Assert.Contains("hasn't added a verified PGP key", MessagesController.CheckPgpBody(Fx("to-alice.asc"), Member("alice", null)));
    }

    [Fact]
    public void Challenge_is_encrypted_to_the_key_and_code_matching_is_forgiving()
    {
        var armored = PgpTools.EncryptTo(Fx("bob.pub.asc"), "code ABC");
        var bobIds = PgpTools.ReadPublicKey(Fx("bob.pub.asc"), out _)!.KeyIds;
        Assert.Equal(PgpMessageCheck.Ok, PgpTools.CheckMessage(armored, bobIds));
        Assert.Equal(PgpMessageCheck.Ok, PgpTools.CheckMessage(PgpTools.EncryptTo(Fx("alice.pub.asc"), "x"), PgpTools.ReadPublicKey(Fx("alice.pub.asc"), out _)!.KeyIds));

        var code = PgpTools.NewChallengeCode();
        Assert.Equal(12, code.Length);
        var hash = PgpTools.HashCode(code);
        Assert.True(PgpTools.CodeMatches(" " + code.ToLowerInvariant().Insert(4, "-") + " ", hash));
        Assert.False(PgpTools.CodeMatches("WRONGCODE123", hash));
        Assert.False(PgpTools.CodeMatches(code, null));
    }
}
