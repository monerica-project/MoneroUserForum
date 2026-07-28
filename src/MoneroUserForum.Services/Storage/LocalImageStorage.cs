using Microsoft.Extensions.Options;

namespace MoneroUserForum.Services.Storage;

/// <summary>On-disk image storage under wwwroot for local development.</summary>
public class LocalImageStorage : IImageStorage
{
    private readonly BunnyStorageOptions options;

    public LocalImageStorage(IOptions<BunnyStorageOptions> options)
    {
        this.options = options.Value;
    }

    public async Task<string> SaveAsync(Stream content, string folder, string fileExtension, CancellationToken ct = default)
    {
        var key = $"{folder.Trim('/')}/{Guid.NewGuid():N}{NormalizeExt(fileExtension)}";
        var full = Path.Combine(this.options.LocalPath, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(storageKey))
        {
            var full = Path.Combine(this.options.LocalPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }

        return Task.CompletedTask;
    }

    public string PublicUrl(string storageKey)
        => string.IsNullOrWhiteSpace(storageKey)
            ? string.Empty
            : $"{this.options.LocalRequestPath.TrimEnd('/')}/{storageKey.TrimStart('/')}";

    private static string NormalizeExt(string ext)
    {
        ext = ext.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? string.Empty : "." + ext;
    }
}
