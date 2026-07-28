namespace MoneroUserForum.Services.Storage;

/// <summary>
/// Bunny.net storage configuration. When <see cref="AccessKey"/> is empty the
/// app falls back to on-disk storage under wwwroot (local dev).
/// </summary>
public class BunnyStorageOptions
{
    public const string SectionName = "BunnyStorage";

    /// <summary>Bunny Storage Zone name.</summary>
    public string StorageZoneName { get; set; } = string.Empty;

    /// <summary>Storage Zone password (write key, used as the AccessKey header for uploads).</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Read-only Storage Zone password, used by the /media proxy to fetch images.</summary>
    public string ReadKey { get; set; } = string.Empty;

    /// <summary>Storage endpoint host, e.g. "storage.bunnycdn.com" or a regional host like "ny.storage.bunnycdn.com".</summary>
    public string StorageHost { get; set; } = "storage.bunnycdn.com";

    /// <summary>Public pull-zone base URL, e.g. "https://monerouserforum.b-cdn.net".</summary>
    public string CdnBaseUrl { get; set; } = string.Empty;

    // ---- Local dev fallback (used when AccessKey is empty) ----
    public string LocalRequestPath { get; set; } = "/uploads";
    public string LocalPath { get; set; } = string.Empty;

    public bool UseBunny => !string.IsNullOrWhiteSpace(this.AccessKey)
                            && !string.IsNullOrWhiteSpace(this.StorageZoneName);
}
