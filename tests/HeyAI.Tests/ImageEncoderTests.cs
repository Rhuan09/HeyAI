using HeyAI.Modules.Vision;
using Windows.Graphics.Imaging;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// Encoding is pure bitmap work with no display involved, so unlike the capture tests
/// these run in CI against a synthetic bitmap.
/// </summary>
public sealed class ImageEncoderTests
{
    private static SoftwareBitmap Bitmap(int width, int height) =>
        new(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

    private static Task<EncodedImage> Encode(SoftwareBitmap b, int max, bool jpeg = false) =>
        ImageEncoder.EncodeAsync(b, max, jpeg, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Downscales_to_the_limit_and_keeps_the_aspect_ratio()
    {
        using var bitmap = Bitmap(1920, 1080);

        var encoded = await Encode(bitmap, 1280);

        Assert.Equal(1280, encoded.Width);
        Assert.Equal(720, encoded.Height);
    }

    [Fact]
    public async Task Does_not_upscale_something_already_smaller()
    {
        // Enlarging costs bytes and adds no detail, so the limit is a ceiling only.
        using var bitmap = Bitmap(320, 200);

        var encoded = await Encode(bitmap, 1280);

        Assert.Equal(320, encoded.Width);
        Assert.Equal(200, encoded.Height);
    }

    [Fact]
    public async Task Clamps_a_dimension_above_the_hard_maximum()
    {
        using var bitmap = Bitmap(4000, 4000);

        var encoded = await Encode(bitmap, 99999);

        Assert.Equal(ImageEncoder.HardMaxDimension, encoded.Width);
    }

    [Fact]
    public async Task Reports_the_mime_type_it_actually_produced()
    {
        using var bitmap = Bitmap(400, 300);

        Assert.Equal("image/png", (await Encode(bitmap, 400)).MimeType);
        Assert.Equal("image/jpeg", (await Encode(bitmap, 400, jpeg: true)).MimeType);
    }

    [Fact]
    public async Task Produces_a_non_empty_payload()
    {
        using var bitmap = Bitmap(400, 300);

        Assert.NotEmpty((await Encode(bitmap, 400)).Bytes);
    }
}
