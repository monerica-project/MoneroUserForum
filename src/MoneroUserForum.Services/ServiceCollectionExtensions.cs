using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoneroUserForum.Services.Notifications;
using MoneroUserForum.Services.Seeding;
using MoneroUserForum.Services.Storage;

namespace MoneroUserForum.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForumServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<BunnyStorageOptions>(config.GetSection(BunnyStorageOptions.SectionName));

        // Bunny in production; on-disk fallback when Bunny isn't configured.
        services.AddHttpClient<BunnyImageStorage>();
        services.AddScoped<IImageStorage>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<BunnyStorageOptions>>().Value;
            return opts.UseBunny
                ? sp.GetRequiredService<BunnyImageStorage>()
                : ActivatorUtilities.CreateInstance<LocalImageStorage>(sp);
        });

        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<DataSeeder>();

        services.Configure<Email.EmailOptions>(config.GetSection(Email.EmailOptions.SectionName));
        services.AddScoped<Email.IEmailSender, Email.SmtpEmailSender>();

        return services;
    }
}
