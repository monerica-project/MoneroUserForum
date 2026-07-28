namespace MoneroUserForum.Services.Storage;

/// <summary>Stores and serves member-uploaded images (avatars, post images).</summary>
public interface IImageStorage
{
    /// <summary>
    /// Saves <paramref name="content"/> under a generated key inside
    /// <paramref name="folder"/> and returns the storage key (path) to persist.
    /// </summary>
    Task<string> SaveAsync(Stream content, string folder, string fileExtension, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Public URL a browser can load for a stored key.</summary>
    string PublicUrl(string storageKey);
}
