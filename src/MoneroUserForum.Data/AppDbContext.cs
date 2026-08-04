using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data;

public class AppDbContext : IdentityDbContext<AppUser, AppRole, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ForumThread> ForumThreads => this.Set<ForumThread>();
    public DbSet<Post> Posts => this.Set<Post>();
    public DbSet<PostImage> PostImages => this.Set<PostImage>();
    public DbSet<PrivateMessage> PrivateMessages => this.Set<PrivateMessage>();
    public DbSet<ChatMessage> ChatMessages => this.Set<ChatMessage>();
    public DbSet<Notification> Notifications => this.Set<Notification>();
    public DbSet<ThreadSubscription> ThreadSubscriptions => this.Set<ThreadSubscription>();
    public DbSet<ThreadView> ThreadViews => this.Set<ThreadView>();
    public DbSet<Report> Reports => this.Set<Report>();
    public DbSet<PostThank> PostThanks => this.Set<PostThank>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(32);
            e.HasIndex(x => x.DisplayName);
        });

        b.Entity<ForumThread>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(220);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.LastPostAtUtc);
            e.HasIndex(x => x.Category);

            e.HasOne(x => x.Author)
                .WithMany(u => u.Threads)
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Post>(e =>
        {
            e.HasIndex(x => new { x.ThreadId, x.CreatedAtUtc });
            e.HasIndex(x => x.AuthorId);

            e.HasOne(x => x.Thread)
                .WithMany(t => t.Posts)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Author)
                .WithMany(u => u.Posts)
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ReplyToPost)
                .WithMany()
                .HasForeignKey(x => x.ReplyToPostId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PostImage>(e =>
        {
            e.HasOne(x => x.Post)
                .WithMany(p => p.Images)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PrivateMessage>(e =>
        {
            e.HasIndex(x => new { x.RecipientId, x.IsReadByRecipient });
            e.HasIndex(x => new { x.SenderId, x.RecipientId, x.CreatedAtUtc });

            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Report>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.ReporterUserId, x.PostId, x.Status });

            e.HasOne(x => x.Thread).WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.RecipientUserId, x.IsRead, x.CreatedAtUtc });

            e.HasOne(x => x.RecipientUser).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Thread).WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ThreadSubscription>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.ThreadId }).IsUnique();

            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Thread).WithMany(t => t.Subscriptions).HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ThreadView>(e =>
        {
            e.Property(x => x.VisitorKey).HasMaxLength(64);
            e.HasIndex(x => new { x.ThreadId, x.VisitorKey }).IsUnique();

            e.HasOne(x => x.Thread).WithMany(t => t.Views).HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PostThank>(e =>
        {
            // One thank per member per post.
            e.HasIndex(x => new { x.PostId, x.UserId }).IsUnique();

            // Fast "thanks received" totals per author.
            e.HasIndex(x => x.PostAuthorId);

            e.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PostAuthor).WithMany().HasForeignKey(x => x.PostAuthorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
