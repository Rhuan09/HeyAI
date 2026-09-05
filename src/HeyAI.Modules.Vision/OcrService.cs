using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace HeyAI.Modules.Vision;

public sealed record OcrLine(string Text, double Top, double Left);

public sealed record OcrOutcome
{
    public required string Language { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyList<OcrLine> Lines { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

/// <summary>
/// Windows' built-in OCR. Offline, already installed, and fast enough that a full 1080p
/// screen comes back in well under a second — which is the reason this module exists
/// rather than shipping a Tesseract binary.
///
/// The engine follows the user's profile languages, so results are localised to whatever
/// they actually read. A machine with no OCR language pack yields no engine at all, which
/// is a real configuration and must degrade to a clear error rather than a crash.
/// </summary>
public sealed class OcrService
{
    /// <summary>
    /// OCR output is text an attacker chose and put on screen. This is the exact input the
    /// read-then-execute block in docs/SECURITY.md exists for.
    /// </summary>
    public const string TaintSource = "ocr-screen-text";

    public async Task<OcrOutcome> RecognizeAsync(SoftwareBitmap bitmap, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new VisionException(
                "No OCR language is installed for this user. Add one under " +
                "Settings > Time & language > Language & region.");

        // The engine caps input dimensions; oversized bitmaps are rejected outright rather
        // than scaled, so say so plainly instead of surfacing an opaque failure.
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension
            || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
        {
            throw new VisionException(
                $"Image is {bitmap.PixelWidth}x{bitmap.PixelHeight}, above the OCR limit of " +
                $"{OcrEngine.MaxImageDimension} pixels per side.");
        }

        var result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);

        var lines = result.Lines
            .Select(l => new OcrLine(
                l.Text,
                l.Words.Count > 0 ? l.Words[0].BoundingRect.Top : 0,
                l.Words.Count > 0 ? l.Words[0].BoundingRect.Left : 0))
            .ToList();

        return new OcrOutcome
        {
            Language = engine.RecognizerLanguage.LanguageTag,
            Text = result.Text,
            Lines = lines,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
        };
    }
}
