using System.Text;
using Kairo.Modules.BrandKit.Files;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class KitFileTypesTests
{
    public static readonly byte[] Ico = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10];

    [Fact]
    public void A_logo_can_be_a_PNG_JPEG_WebP_SVG_or_icon()
    {
        Assert.Equal("image/png", KitFileTypes.DetectLogo(BrandKits.Png));
        Assert.Equal("image/jpeg", KitFileTypes.DetectLogo([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/webp", KitFileTypes.DetectLogo("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.Equal("image/svg+xml", KitFileTypes.DetectLogo(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));
        Assert.Equal("image/x-icon", KitFileTypes.DetectLogo(Ico));
    }

    [Fact]
    public void Documents_and_web_pages_are_not_logos()
    {
        Assert.Null(KitFileTypes.DetectLogo(BrandKits.Pdf));
        Assert.Null(KitFileTypes.DetectLogo(Encoding.UTF8.GetBytes("<html><body>Not found</body></html>")));
    }

    [Fact]
    public void Uploads_still_refuse_icons()
    {
        Assert.Null(KitFileTypes.Detect(Ico, "favicon.ico"));
    }
}
