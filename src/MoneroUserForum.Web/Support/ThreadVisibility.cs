using System.Linq.Expressions;
using MoneroUserForum.Data.Entities;

namespace MoneroUserForum.Web.Support;

/// <summary>
/// The single rule for who may see a thread. Every place that reveals a thread (lists, the
/// sitemap, and the redirects after replying, thanking or reporting, which put the slug, and so
/// the title, in the URL) uses these, so a censored thread or one by a not-yet-approved member
/// cannot leak through a side door.
/// </summary>
public static class ThreadVisibility
{
    /// <summary>Visible to everyone: live, not censored, and the opening author is approved and not deleted.</summary>
    public static readonly Expression<Func<ForumThread, bool>> IsPublic =
        t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted && t.Author!.IsApproved;

    /// <summary>
    /// What one viewer may open: admins see censored and pending threads too; a pending thread's
    /// own author sees it (so they can read their post); everyone else gets only public threads.
    /// </summary>
    public static Expression<Func<ForumThread, bool>> VisibleTo(int? viewerId, bool isAdmin)
    {
        if (isAdmin)
        {
            return t => !t.IsDeleted && !t.Author!.IsDeleted;
        }

        return t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted
                    && (t.Author!.IsApproved || t.AuthorId == viewerId);
    }
}
