using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services;
using MoneroUserForum.Services.Seeding;
using MoneroUserForum.Web.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// All site URLs are canonical lowercase: tag helpers / Url.Action emit lowercase paths.
// (Query strings are left as-is.) Inbound mixed-case paths are 301'd to lowercase below.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteOptions>(options => options.LowercaseUrls = true);

// Real-time chatroom (SignalR, transport auto-fallback so it works over Tor).
builder.Services.AddSignalR();

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpContextAccessor();

// ---- Data Protection: persist keys so logins survive redeploys ----
// (rsync wipes the deploy folder, so the key ring must live outside it — set
// DataProtection:KeyRingPath in production, see CI/deploy.sh.)
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (string.IsNullOrWhiteSpace(keyRingPath))
{
    keyRingPath = Path.Combine(builder.Environment.ContentRootPath, "..", "monerouserforum-keys");
}

Directory.CreateDirectory(keyRingPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("MoneroUserForum");

// ---- Identity: username + password, no email required ----
builder.Services
    .AddIdentity<AppUser, AppRole>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-";
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 8;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// Refresh the signed-in principal's claims on every request so an admin
// approving (or banning) a member takes effect immediately on their next page
// load — no sign-out required. (We do NOT bump the security stamp on approve,
// which would instead invalidate their cookie and log them out.)
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.Zero);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "muf.auth";
});

// ---- Authorization policies ----
builder.Services.AddAuthorization(options =>
{
    // Signed in, admin-approved, and not banned — required to create content.
    options.AddPolicy(Policies.CanPost, p => p
        .RequireAuthenticatedUser()
        .RequireClaim("approved", "true")
        .RequireAssertion(ctx => !ctx.User.HasClaim("banned", "true")));

    // Post/reply without waiting for approval. Not banned is still required; posts by an
    // unapproved member are stored but hidden from others until their account is approved.
    options.AddPolicy(Policies.CanContribute, p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => !ctx.User.HasClaim("banned", "true")));

    options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
});

// Session (used by the sign-up captcha)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(20);
});

// ---- Application services (storage, notifications, seeder) ----
builder.Services.AddForumServices(builder.Configuration);

// XMR price: fetched server-side from moneropricenow.com once a minute and cached, so
// it renders without JavaScript. (moneropricenow.com serves it from its own 15s cache.)
builder.Services.AddHttpClient("moneroprice", c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("MoneroUserForum/1.0 (+https://monerouserforum.com)");
});
builder.Services.AddSingleton<MoneroUserForum.Web.Pricing.MoneroPriceService>();
builder.Services.AddHostedService<MoneroUserForum.Web.Pricing.MoneroPriceRefresher>();

// When Bunny isn't configured, images fall back to on-disk storage: point it at
// wwwroot/uploads (served as /uploads) so avatars actually save + serve.
builder.Services.PostConfigure<MoneroUserForum.Services.Storage.BunnyStorageOptions>(o =>
{
    if (string.IsNullOrWhiteSpace(o.LocalPath))
    {
        var webRoot = builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
        o.LocalPath = Path.Combine(webRoot, o.LocalRequestPath.Trim('/'));
    }
});

// ---- Sponsor ads (Monerica feed, cached, server-rendered so no-JS works) ----
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("sponsors");
builder.Services.AddSingleton<MoneroUserForum.Web.Support.SponsorService>();

// Behind nginx (TLS terminated there): trust the forwarded proto so Request.Scheme
// is https for absolute URLs (e.g. password-reset links).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Security headers, incl. a strict Content-Security-Policy. script-src 'self'
// (no unsafe-inline) means an injected inline <script> or on*= handler simply
// cannot run — defence in depth on top of output-encoding + post sanitization.
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        // frame-src: only the privacy-enhanced youtube-nocookie domain, and only
        // after the user clicks the click-to-play facade (nothing loads otherwise).
        "frame-src https://www.youtube-nocookie.com; " +
        "img-src 'self' https: data:; font-src 'self'; connect-src 'self'";
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["X-Frame-Options"] = "DENY";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseHttpsRedirection();
app.UseStaticFiles();

// Canonicalize to lowercase: 301 any GET/HEAD whose PATH contains uppercase letters to the
// lowercased path (query string preserved). Runs after UseStaticFiles so on-disk assets are
// served untouched; only routed requests (e.g. /thread/category/BusinessDiscussion) are redirected.
app.Use(async (context, next) =>
{
    var request = context.Request;
    var path = request.Path.Value;
    if (!string.IsNullOrEmpty(path)
        && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
    {
        var lower = path.ToLowerInvariant();
        if (!string.Equals(path, lower, StringComparison.Ordinal))
        {
            context.Response.Redirect(lower + request.QueryString.Value, permanent: true);
            return;
        }
    }

    await next();
});

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<MoneroUserForum.Web.Hubs.ChatHub>("/hubs/chat");

// ---- Migrate + seed on startup ----
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await sp.GetRequiredService<DataSeeder>().SeedAsync();
}

app.Run();
