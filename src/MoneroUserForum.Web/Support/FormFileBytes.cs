namespace MoneroUserForum.Web.Support;

/// <summary>Reads an uploaded file into memory (uploads are size-capped before this is called).</summary>
public static class FormFileBytes
{
    public static async Task<byte[]> ReadAllAsync(IFormFile file, CancellationToken ct = default)
    {
        using var ms = new MemoryStream((int)file.Length);
        await using var s = file.OpenReadStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
