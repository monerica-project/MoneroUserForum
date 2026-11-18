using Microsoft.AspNetCore.Http;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Email;
using MoneroUserForum.Services.Text;
using MoneroUserForum.Web.Support;
using Xunit;

namespace MoneroUserForum.Tests;

public class SiteLinksTests
{
    private static HttpRequest RequestFor(string host)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(host);
        return ctx.Request;
    }

    private static EmailOptions Options(string onion = "") =>
        new() { SiteUrl = "https://monerouserforum.com/", OnionHost = onion };

    [Theory]
    [InlineData("evil.example")]
    [InlineData("attackerattackerattacker.onion")]
    [InlineData("monerouserforum.com.evil.example")]
    public void Forged_host_headers_never_reach_links(string host)
    {
        var url = SiteLinks.BaseUrl(RequestFor(host), Options("forumforumforum.onion"));
        Assert.Equal("https://monerouserforum.com", url);
    }

    [Fact]
    public void The_configured_onion_host_gets_onion_links()
    {
        var url = SiteLinks.BaseUrl(RequestFor("ForumForumForum.onion"), Options("forumforumforum.onion"));
        Assert.Equal("http://forumforumforum.onion", url);
    }

    [Fact]
    public void Without_a_configured_onion_host_any_onion_falls_back_to_the_site_url()
    {
        Assert.Equal("https://monerouserforum.com", SiteLinks.BaseUrl(RequestFor("someone.onion"), Options()));
    }
}

public class ThreadVisibilityTests
{
    private static ForumThread Thread(bool approved = true, bool censored = false, bool deleted = false, bool authorDeleted = false, int authorId = 7) =>
        new()
        {
            AuthorId = authorId,
            IsCensored = censored,
            IsDeleted = deleted,
            Author = new AppUser { Id = authorId, IsApproved = approved, IsDeleted = authorDeleted },
        };

    private static readonly Func<ForumThread, bool> IsPublic = ThreadVisibility.IsPublic.Compile();

    [Fact]
    public void Sitemap_and_lists_show_a_normal_thread() => Assert.True(IsPublic(Thread()));

    [Fact]
    public void Sitemap_and_lists_hide_censored_threads() => Assert.False(IsPublic(Thread(censored: true)));

    [Fact]
    public void Sitemap_and_lists_hide_threads_by_unapproved_members() => Assert.False(IsPublic(Thread(approved: false)));

    [Fact]
    public void Sitemap_and_lists_hide_deleted_threads()
    {
        Assert.False(IsPublic(Thread(deleted: true)));
        Assert.False(IsPublic(Thread(authorDeleted: true)));
    }

    [Fact]
    public void A_pending_thread_is_visible_only_to_its_author_and_admins()
    {
        var pending = Thread(approved: false, authorId: 7);
        Assert.True(ThreadVisibility.VisibleTo(7, false).Compile()(pending));
        Assert.False(ThreadVisibility.VisibleTo(8, false).Compile()(pending));
        Assert.False(ThreadVisibility.VisibleTo(null, false).Compile()(pending));
        Assert.True(ThreadVisibility.VisibleTo(8, true).Compile()(pending));
    }

    [Fact]
    public void A_censored_thread_is_hidden_even_from_its_author_but_not_from_admins()
    {
        var censored = Thread(censored: true, authorId: 7);
        Assert.False(ThreadVisibility.VisibleTo(7, false).Compile()(censored));
        Assert.True(ThreadVisibility.VisibleTo(1, true).Compile()(censored));
    }
}

public class VisitorCookieTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("0123456789abcdef0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    public void Missing_or_foreign_cookies_do_not_count_a_view(string? value) => Assert.Null(VisitorCookie.ReadExisting(value));

    [Fact]
    public void A_key_we_issued_counts()
    {
        var key = VisitorCookie.NewKey();
        Assert.Equal(key, VisitorCookie.ReadExisting(key));
    }
}

public class PostingLimitTests
{
    [Fact]
    public void Mentions_are_capped_at_five_distinct_members()
    {
        var body = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"@member{i:00}"));
        Assert.Equal(MentionParser.MaxMentionsPerPost, MentionParser.Extract(body).Count);
    }

    [Fact]
    public void Unapproved_members_hit_their_lower_cap()
    {
        var guard = new SignupGuard();
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("approved", "false") }, "test"));

        for (var i = 0; i < PostingLimits.UnapprovedThreadsPerHour; i++)
        {
            Assert.True(PostingLimits.TryCount(guard, user, "42", "thread", PostingLimits.ThreadsPerHour, PostingLimits.UnapprovedThreadsPerHour));
        }

        Assert.False(PostingLimits.TryCount(guard, user, "42", "thread", PostingLimits.ThreadsPerHour, PostingLimits.UnapprovedThreadsPerHour));

        // Another member's allowance is separate.
        Assert.True(PostingLimits.TryCount(guard, user, "43", "thread", PostingLimits.ThreadsPerHour, PostingLimits.UnapprovedThreadsPerHour));
    }
}
