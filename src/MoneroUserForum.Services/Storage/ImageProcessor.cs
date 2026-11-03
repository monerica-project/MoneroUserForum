using SkiaSharp;

namespace MoneroUserForum.Services.Storage;

/// <summary>What an uploaded image will be used for. Decides the output size.</summary>
public enum ImagePurpose
{
    /// <summary>Profile picture: center-cropped to a small square.</summary>
    Avatar,

    /// <summary>Image attached to a post: shrunk to fit, aspect ratio kept.</summary>
    PostImage,
}

/// <summary>Result of <see cref="ImageProcessor.Process"/>. Either bytes to store or an error for the member.</summary>
public sealed record ImageProcessResult(byte[]? Bytes, string? Extension, string? Error)
{
    public bool Ok => this.Error is null;

    public static ImageProcessResult Fail(string error) => new(null, null, error);
}

/// <summary>
/// Makes member uploads small and safe to publish. Still images are re-encoded as WebP,
/// which drops ALL metadata (GPS location, camera, timestamps), after applying the EXIF
/// rotation so photos don't end up sideways. Animated GIF/WebP can't be re-encoded with
/// SkiaSharp, so they're kept as-is up to <see cref="AnimatedMaxBytes"/> (WebP EXIF/XMP
/// chunks are still removed).
/// </summary>
public static class ImageProcessor
{
    public const int AnimatedMaxBytes = 500 * 1024;
    public const int AvatarSide = 256;
    public const int PostImageMaxSide = 1600;
    public const int WebpQuality = 80;

    // Refuse decompression bombs before decoding (a tiny file can claim huge dimensions).
    private const long MaxSourcePixels = 50_000_000;

    public static ImageProcessResult Process(byte[] input, ImagePurpose purpose)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return ImageProcessResult.Fail("That file isn't a readable image.");
        }

        if (codec.FrameCount > 1)
        {
            return ProcessAnimated(input, codec.EncodedFormat);
        }

        var info = codec.Info;
        if ((long)info.Width * info.Height > MaxSourcePixels)
        {
            return ImageProcessResult.Fail("That image is too large. Please use one under 50 megapixels.");
        }

        using var decoded = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (decoded is null)
        {
            return ImageProcessResult.Fail("That file isn't a readable image.");
        }

        using var output = Render(decoded, codec.EncodedOrigin, purpose);
        using var image = SKImage.FromBitmap(output);
        using var webp = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        return webp is null
            ? ImageProcessResult.Fail("That image couldn't be processed.")
            : new ImageProcessResult(webp.ToArray(), ".webp", null);
    }

    /// <summary>
    /// Matrix mapping source pixels to their upright position for an EXIF orientation.
    /// <paramref name="w"/>/<paramref name="h"/> are the stored (un-rotated) dimensions.
    /// </summary>
    public static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    /// <summary>Rotates upright, then scales (and for avatars, center-crops) in one draw.</summary>
    public static SKBitmap Render(SKBitmap src, SKEncodedOrigin origin, ImagePurpose purpose)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var uprightW = swap ? src.Height : src.Width;
        var uprightH = swap ? src.Width : src.Height;

        int outW, outH;
        float scale, offsetX = 0, offsetY = 0;
        if (purpose == ImagePurpose.Avatar)
        {
            var shortSide = Math.Min(uprightW, uprightH);
            var side = Math.Min(AvatarSide, shortSide);
            scale = side / (float)shortSide;
            outW = outH = side;
            offsetX = (side - (uprightW * scale)) / 2f;
            offsetY = (side - (uprightH * scale)) / 2f;
        }
        else
        {
            var longSide = Math.Max(uprightW, uprightH);
            scale = Math.Min(1f, PostImageMaxSide / (float)longSide);
            outW = Math.Max(1, (int)Math.Round(uprightW * scale));
            outH = Math.Max(1, (int)Math.Round(uprightH * scale));
        }

        var matrix = SKMatrix.CreateTranslation(offsetX, offsetY)
            .PreConcat(SKMatrix.CreateScale(scale, scale))
            .PreConcat(OriginMatrix(origin, src.Width, src.Height));

        var dst = new SKBitmap(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        using var img = SKImage.FromBitmap(src);
        canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();
        return dst;
    }

    private static ImageProcessResult ProcessAnimated(byte[] input, SKEncodedImageFormat format)
    {
        if (input.Length > AnimatedMaxBytes)
        {
            return ImageProcessResult.Fail("Animated images must be 500 KB or smaller.");
        }

        return format switch
        {
            SKEncodedImageFormat.Gif => new ImageProcessResult(input, ".gif", null),
            SKEncodedImageFormat.Webp => new ImageProcessResult(StripWebpMetadata(input), ".webp", null),
            _ => ImageProcessResult.Fail("Animated images must be GIF or WebP."),
        };
    }

    /// <summary>Removes EXIF and XMP chunks from a WebP (RIFF) file and clears their VP8X flags.</summary>
    public static byte[] StripWebpMetadata(byte[] webp)
    {
        if (webp.Length < 12 || webp[0] != 'R' || webp[1] != 'I' || webp[2] != 'F' || webp[3] != 'F'
            || webp[8] != 'W' || webp[9] != 'E' || webp[10] != 'B' || webp[11] != 'P')
        {
            return webp;
        }

        using var ms = new MemoryStream(webp.Length);
        ms.Write(webp, 0, 12);
        var pos = 12;
        while (pos + 8 <= webp.Length)
        {
            var fourCc = System.Text.Encoding.ASCII.GetString(webp, pos, 4);
            var size = BitConverter.ToInt32(webp, pos + 4);
            var padded = size + (size & 1);
            if (size < 0 || pos + 8 + padded > webp.Length)
            {
                // Truncated or odd file: copy the rest unchanged rather than corrupt it.
                ms.Write(webp, pos, webp.Length - pos);
                break;
            }

            if (fourCc is not ("EXIF" or "XMP "))
            {
                var start = (int)ms.Position;
                ms.Write(webp, pos, 8 + padded);
                if (fourCc == "VP8X" && size >= 1)
                {
                    var buf = ms.GetBuffer();
                    buf[start + 8] &= unchecked((byte)~(0x08 | 0x04)); // EXIF + XMP flags
                }
            }

            pos += 8 + padded;
        }

        var result = ms.ToArray();
        BitConverter.GetBytes(result.Length - 8).CopyTo(result, 4); // RIFF size
        return result;
    }
}
