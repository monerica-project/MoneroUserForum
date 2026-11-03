using System.Text;
using MoneroUserForum.Services.Storage;
using SkiaSharp;
using Xunit;

namespace MoneroUserForum.Tests;

public class ImageProcessorTests
{
    [Fact]
    public void Jpeg_with_exif_is_reencoded_as_webp_without_metadata_and_rotated_upright()
    {
        // 400x200 stored, EXIF orientation 6 (rotate 90° CW) => 200x400 upright.
        var jpeg = WithExif(Encode(Solid(400, 200, SKColors.Red), SKEncodedImageFormat.Jpeg), orientation: 6, secret: "SECRET-GPS-48.8584N");

        var r = ImageProcessor.Process(jpeg, ImagePurpose.PostImage);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(".webp", r.Extension);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(r.Bytes!, 0, 4));
        Assert.DoesNotContain("SECRET-GPS", Encoding.ASCII.GetString(r.Bytes!));
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(r.Bytes!));
        using var codec = SKCodec.Create(new MemoryStream(r.Bytes!));
        Assert.Equal(200, codec.Info.Width);
        Assert.Equal(400, codec.Info.Height);
    }

    [Fact]
    public void Large_post_image_is_shrunk_to_max_side_keeping_aspect()
    {
        var png = Encode(Solid(4000, 1000, SKColors.Blue), SKEncodedImageFormat.Png);

        var r = ImageProcessor.Process(png, ImagePurpose.PostImage);

        Assert.True(r.Ok, r.Error);
        using var codec = SKCodec.Create(new MemoryStream(r.Bytes!));
        Assert.Equal(ImageProcessor.PostImageMaxSide, codec.Info.Width);
        Assert.Equal(400, codec.Info.Height);
        Assert.True(r.Bytes!.Length < png.Length);
    }

    [Fact]
    public void Small_post_image_is_not_enlarged()
    {
        var r = ImageProcessor.Process(Encode(Solid(300, 120, SKColors.Green), SKEncodedImageFormat.Png), ImagePurpose.PostImage);

        using var codec = SKCodec.Create(new MemoryStream(r.Bytes!));
        Assert.Equal(300, codec.Info.Width);
        Assert.Equal(120, codec.Info.Height);
    }

    [Fact]
    public void Avatar_is_center_cropped_to_square()
    {
        var r = ImageProcessor.Process(Encode(Solid(900, 300, SKColors.Orange), SKEncodedImageFormat.Png), ImagePurpose.Avatar);

        Assert.True(r.Ok, r.Error);
        using var codec = SKCodec.Create(new MemoryStream(r.Bytes!));
        Assert.Equal(ImageProcessor.AvatarSide, codec.Info.Width);
        Assert.Equal(ImageProcessor.AvatarSide, codec.Info.Height);
    }

    [Fact]
    public void Small_animated_gif_is_kept_as_is()
    {
        var gif = AnimatedGif(paddingBytes: 0);

        var r = ImageProcessor.Process(gif, ImagePurpose.Avatar);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(".gif", r.Extension);
        Assert.Equal(gif, r.Bytes);
    }

    [Fact]
    public void Animated_gif_over_500kb_is_rejected()
    {
        var gif = AnimatedGif(paddingBytes: ImageProcessor.AnimatedMaxBytes + 10);

        var r = ImageProcessor.Process(gif, ImagePurpose.Avatar);

        Assert.False(r.Ok);
        Assert.Contains("500 KB", r.Error);
    }

    [Fact]
    public void Non_image_is_rejected()
    {
        var r = ImageProcessor.Process(Encoding.ASCII.GetBytes("<html>not an image</html>"), ImagePurpose.PostImage);

        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData(SKEncodedOrigin.RightTop, 1, 0)]   // rotate 90° CW: top-left goes to top-right
    [InlineData(SKEncodedOrigin.LeftBottom, 0, 2)] // rotate 90° CCW: top-left goes to bottom-left
    [InlineData(SKEncodedOrigin.BottomRight, 2, 1)]
    [InlineData(SKEncodedOrigin.TopLeft, 0, 0)]
    public void Orientation_moves_top_left_pixel_to_expected_corner(SKEncodedOrigin origin, int x, int y)
    {
        // Stored 3x2 (or upright 2x3 when rotated), single red pixel at stored (0,0).
        using var src = Solid(3, 2, SKColors.Black);
        src.SetPixel(0, 0, SKColors.Red);

        using var outBmp = ImageProcessor.Render(src, origin, ImagePurpose.PostImage);

        var red = outBmp.GetPixel(x, y);
        Assert.True(red.Red > 200 && red.Green < 60, $"expected red at ({x},{y}) for {origin}, got {red}");
    }

    [Fact]
    public void Webp_metadata_chunks_are_removed_and_flags_cleared()
    {
        var vp8x = Chunk("VP8X", [0x08 | 0x04 | 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var exif = Chunk("EXIF", Encoding.ASCII.GetBytes("SECRET-GPS"));
        var xmp = Chunk("XMP ", Encoding.ASCII.GetBytes("<x>secret</x>"));
        var anim = Chunk("ANIM", new byte[6]);
        var body = Encoding.ASCII.GetBytes("WEBP").Concat(vp8x).Concat(exif).Concat(anim).Concat(xmp).ToArray();
        var file = Encoding.ASCII.GetBytes("RIFF").Concat(BitConverter.GetBytes(body.Length)).Concat(body).ToArray();

        var stripped = ImageProcessor.StripWebpMetadata(file);
        var text = Encoding.ASCII.GetString(stripped);

        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("XMP ", text);
        Assert.Contains("ANIM", text);
        Assert.Equal(0x02, stripped[20]); // only the animation flag left
        Assert.Equal(stripped.Length - 8, BitConverter.ToInt32(stripped, 4));
    }

    private static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h);
        b.Erase(c);
        return b;
    }

    private static byte[] Encode(SKBitmap b, SKEncodedImageFormat f)
    {
        using (b)
        using (var img = SKImage.FromBitmap(b))
        using (var d = img.Encode(f, 90))
        {
            return d.ToArray();
        }
    }

    /// <summary>Inserts an EXIF APP1 segment (orientation + an ImageDescription string) after SOI.</summary>
    private static byte[] WithExif(byte[] jpeg, ushort orientation, string secret)
    {
        var desc = Encoding.ASCII.GetBytes(secret + "\0");
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes((ushort)42));
        tiff.AddRange(BitConverter.GetBytes(8u));
        tiff.AddRange(BitConverter.GetBytes((ushort)2)); // 2 entries
        var dataOffset = 8u + 2 + (2 * 12) + 4;
        tiff.AddRange(BitConverter.GetBytes((ushort)0x010E)); // ImageDescription
        tiff.AddRange(BitConverter.GetBytes((ushort)2));
        tiff.AddRange(BitConverter.GetBytes((uint)desc.Length));
        tiff.AddRange(BitConverter.GetBytes(dataOffset));
        tiff.AddRange(BitConverter.GetBytes((ushort)0x0112)); // Orientation
        tiff.AddRange(BitConverter.GetBytes((ushort)3));
        tiff.AddRange(BitConverter.GetBytes(1u));
        tiff.AddRange(BitConverter.GetBytes(orientation));
        tiff.AddRange(new byte[2]);
        tiff.AddRange(BitConverter.GetBytes(0u));
        tiff.AddRange(desc);

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var len = payload.Length + 2;
        var app1 = new byte[] { 0xFF, 0xE1, (byte)(len >> 8), (byte)(len & 0xFF) }.Concat(payload);
        return jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).ToArray();
    }

    /// <summary>Hand-built 1x1, 2-frame GIF, optionally padded with comment blocks to reach a size.</summary>
    private static byte[] AnimatedGif(int paddingBytes)
    {
        var g = new List<byte>();
        g.AddRange("GIF89a"u8.ToArray());
        g.AddRange([1, 0, 1, 0, 0x80, 0, 0]);            // 1x1, global table of 2 colors
        g.AddRange([0, 0, 0, 255, 255, 255]);
        g.AddRange([0x21, 0xFF, 0x0B]);
        g.AddRange("NETSCAPE2.0"u8.ToArray());
        g.AddRange([3, 1, 0, 0, 0]);                      // loop forever
        for (var i = 0; i < 2; i++)
        {
            g.AddRange([0x21, 0xF9, 4, 0, 10, 0, 0, 0]); // 0.1s delay
            g.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
            g.AddRange([2, 2, 0x44, 0x01, 0]);
        }

        if (paddingBytes > 0)
        {
            g.AddRange([0x21, 0xFE]);
            for (var left = paddingBytes; left > 0; left -= 255)
            {
                var n = Math.Min(255, left);
                g.Add((byte)n);
                g.AddRange(Enumerable.Repeat((byte)'x', n));
            }

            g.Add(0);
        }

        g.Add(0x3B);
        return g.ToArray();
    }

    private static byte[] Chunk(string fourCc, byte[] data)
    {
        var c = Encoding.ASCII.GetBytes(fourCc).Concat(BitConverter.GetBytes(data.Length)).Concat(data);
        return (data.Length % 2 == 1 ? c.Append((byte)0) : c).ToArray();
    }
}
