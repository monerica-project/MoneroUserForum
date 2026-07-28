# Monero User Forum

A privacy-first discussion forum for Monero users — price talk, technical help, and
general discussion. **No email required**, works over Tor, and self-hostable.

Live: [monerouserforum.com](https://monerouserforum.com) · part of the Monerica network.

## Features

- **Accounts without email** — sign up with a username + a one-time access key (no
  email, optional email only for self-service password reset).
- **Admin-gated membership** — new accounts are approved by an admin before they can post.
- **Threads & categories**, `@mentions`, replies, thread subscriptions, and in-app
  notifications (no email).
- **Private messages** with a two-pane messenger, and a **real-time chatroom**
  (SignalR with WebSocket → SSE → long-polling fallback, so it works over Tor).
- **Moderation** — ban (posts blacked out), censor a post and its replies (or a whole
  thread), and a **report → admin review queue** for spam and problem posts.
- **Reputation levels**, per-thread unique view counts (cookie-based, **no IP logging**).
- **Bunny.net** image storage for avatars, with an on-disk fallback for local dev.
- Hardened by default: strict Content-Security-Policy, output encoding + HTML
  sanitisation on all user content, antiforgery on every mutation.

## Tech stack

- ASP.NET Core MVC (.NET 10), Razor views
- Entity Framework Core + PostgreSQL (Npgsql)
- ASP.NET Core Identity (username-based)
- SignalR for the real-time chatroom
- Markdig + HtmlSanitizer for safe post rendering

## Local development

Requirements: .NET 10 SDK and a local PostgreSQL.

```bash
# 1) Create a local database (defaults below match appsettings.json)
#    Host=localhost;Port=5432;Database=monerouserforum;Username=postgres;Password=postgres

# 2) Run — EF migrations are applied automatically on startup
dotnet run --project src/MoneroUserForum.Web
```

The app listens on the URLs in `src/MoneroUserForum.Web/Properties/launchSettings.json`.
Without Bunny configured, uploaded images fall back to `wwwroot/uploads/`.

## Configuration & secrets

**No secrets are committed to this repo.** The values in `appsettings.json` are empty
placeholders or local-dev defaults; real values are supplied via **environment
variables** in production (double-underscore maps to config sections):

| Setting | Env var | Notes |
| --- | --- | --- |
| Database | `ConnectionStrings__Default` | Postgres connection string |
| Bunny storage zone | `BunnyStorage__StorageZoneName` | leave empty to use on-disk storage |
| Bunny write key | `BunnyStorage__AccessKey` | |
| Bunny read key | `BunnyStorage__ReadKey` | |
| Bunny CDN base URL | `BunnyStorage__CdnBaseUrl` | e.g. `https://<zone>.b-cdn.net` |
| Data-protection key path | `DataProtection__KeyRingPath` | persist keys across redeploys |
| Seed admin username | `Seed__AdminUsername` | first-run admin bootstrap |
| Seed admin password | `Seed__AdminPassword` | |

In the reference deployment these live in a root-only `/etc/monerouserforum/db.env`
loaded by the systemd unit — never in the repo. `.gitignore` is configured to keep
`*.env`, `appsettings.Production.json`, key files, and the data-protection key ring
out of version control.

## Project layout

```
src/
  MoneroUserForum.Data      — entities, DbContext, EF migrations
  MoneroUserForum.Services  — storage, notifications, reputation, text/markup
  MoneroUserForum.Web       — controllers, views, hubs, wwwroot
CI/deploy.sh                — publish + deploy to the reference host
```

## License

No license has been chosen yet — see the repository owner.
