using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace HeyAI.Modules.Vision;

public sealed record EncodedImage(byte[] Bytes, string MimeType, int Width, int Height);

/// <summary>
/// Turns a captured bitmap into something small enough to put in a model's context.
///
/// This is mostly a size problem. A raw 1920x1080 BGRA frame is 8 MB, and even as PNG a
/// busy desktop lands around 1-2 MB — which becomes a third more again as base64, in
/// every message of the conversation from then on. Downscaling first is what makes the
/// tool usable rather than merely possible.
/// </summary>
public static class ImageEncoder
{
    /// <summary>
    /// Beyond this, more pixels stop buying the model more understanding and only cost
    /// context. Roughly the point where UI text is still legible after scaling.
    /// </summary>
    public const int DefaultMaxDimension = 1280;

    public const int HardMaxDimension = 1920;

    public static async Task<EncodedImage> EncodeAsync(
        SoftwareBitmap bitmap, int maxDimension, bool preferJpeg, CancellationToken ct)
    {
        maxDimension = Math.Clamp(maxDimension, 160, HardMaxDimension);

        var scale = Math.Min(
            1.0,
            (double)maxDimension / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));

        var width = Math.Max(1, (int)Math.Round(bitmap.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.PixelHeight * scale));

        // JPEG has no alpha channel, so the mode has to be Ignore or the encoder rejects
        // the bitmap. PNG keeps premultiplied alpha, which is what capture produces.
        var alpha = preferJpeg ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Premultiplied;
        using var normalized = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, alpha);

        using var stream = new InMemoryRandomAccessStream();

        var encoderId = preferJpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId;
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream).AsTask(ct).ConfigureAwait(false);

        encoder.SetSoftwareBitmap(normalized);
        encoder.BitmapTransform.ScaledWidth = (uint)width;
        encoder.BitmapTransform.ScaledHeight = (uint)height;

        // Fant is slower than Linear but keeps small text readable when downscaling, which
        // is the whole point of sending a screenshot to a model.
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        encoder.IsThumbnailGenerated = false;

        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);

        var bytes = new byte[stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size).AsTask(ct).ConfigureAwait(false);
            reader.ReadBytes(bytes);
        }

        return new EncodedImage(bytes, preferJpeg ? "image/jpeg" : "image/png", width, height);
    }
}
