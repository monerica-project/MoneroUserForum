using MoneroUserForum.Web.Support;
using Xunit;

namespace MoneroUserForum.Tests;

public class SignupGuardTests
{
    [Theory]
    [InlineData("Researcher looking at the forum software for an authorised assessment")]
    [InlineData("Benign enumeration probe from a security assessment, please ignore")]
    [InlineData("Second probe account for IDOR/reset testing on this authorised test")]
    [InlineData("live PGP build probe, authorized assessment")]
    [InlineData("Audit472 enumeration and registration proof note.")]
    [InlineData("I am a QA tester, interested in Monero privacy tooling")]
    [InlineData("qa1390 registered for injection testing")]
    [InlineData("qa1390 mass assignment probe")]
    [InlineData("security research audit account for reading public forum data")]
    [InlineData("Security audit of the Monero network, requesting an account")]
    [InlineData("zzprobe9k")]
    [InlineData("sig<b>B</b>")]
    public void Scanner_join_notes_are_refused(string text) => Assert.True(SignupGuard.LooksAutomated(text));

    [Theory]
    [InlineData("I run a node and want to talk about wallets and mining with other users.")]
    [InlineData("Long-time XMR holder, found the forum through Monerica.")]
    [InlineData("I want to ask about audits of the Monero codebase and bulletproofs.")]
    [InlineData("nebuchadnezzar_ii")]
    [InlineData("")]
    public void Real_join_notes_pass(string text) => Assert.False(SignupGuard.LooksAutomated(text));

    [Theory]
    [InlineData("qa1689727052@example.com", true)]
    [InlineData("x@something.invalid", true)]
    [InlineData("me@proton.me", false)]
    [InlineData(null, false)]
    public void Test_email_domains(string? email, bool expected) => Assert.Equal(expected, SignupGuard.IsThrowawayTestEmail(email));

    [Fact]
    public void Hourly_ceiling_stops_a_flood()
    {
        var guard = new SignupGuard();
        for (var i = 0; i < SignupGuard.RegistrationsPerHour; i++)
        {
            Assert.True(guard.TryCount("register", SignupGuard.RegistrationsPerHour));
        }

        Assert.False(guard.TryCount("register", SignupGuard.RegistrationsPerHour));
        Assert.True(guard.TryCount("contact", SignupGuard.ContactMessagesPerHour));
    }
}
