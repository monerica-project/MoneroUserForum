using QRCoder;

namespace MoneroUserForum.Web.Support;

/// <summary>Generates tip QR codes for member profiles.</summary>
public static class QrCodeHelper
{
    /// <summary>
    /// Renders a Monero tip QR as a base64 PNG data URI. The payload is a
    /// <c>monero:&lt;address&gt;</c> URI, which wallets recognise to pre-fill the send
    /// screen. Returns null for a blank address. Uses QRCoder's fully-managed PNG
    /// encoder (no System.Drawing), so it runs on Linux and needs no external calls —
    /// safe under the strict CSP (the data: URI is allowed by img-src).
    /// </summary>
    public static string? MoneroTipDataUri(string? address, int pixelsPerModule = 6)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode("monero:" + address.Trim(), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }
}
