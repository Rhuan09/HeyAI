using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Modules.Vision.Tools;

public sealed class ScreenCaptureTool(CaptureService capture) : HeyAITool
{
    /// <summary>
    /// Refuse rather than send something this large. Base64 adds a third again on top,
    /// and an oversized image sits in every subsequent message of the conversation.
    /// </summary>
    private const int MaxBytes = 4 * 1024 * 1024;

    public override string Name => "screen_capture";
    public override string Title => "Capture the screen as an image";

    public override string Description =>
        "Captures the primary monitor, or one window, and returns it as an image you can " +
        "look at. Prefer ocr_read_text when you only need the words: it returns far less " +
        "data. Use this when layout, colour, or a non-text element matters. Pass hwnd " +
        "from window_list_open to capture a single window. Read-only.";

    public override ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "hwnd": {
              "type": "integer",
              "description": "Handle of a window to capture, from window_list_open. Omit to capture the whole primary monitor."
            },
            "maxDimension": {
              "type": "integer",
              "minimum": 160,
              "maximum": 1920,
              "description": "Longest side of the returned image in pixels. Defaults to 1280. Lower it if the result is rejected as too large, or to spend less context."
            },
            "format": {
              "type": "string",
              "enum": ["png", "jpeg"],
              "description": "png keeps small text crisp; jpeg is about half the size. Defaults to png. For scale: a 1080p desktop comes back around 440KB as png at the default size, 216KB as jpeg, and 99KB as jpeg at maxDimension 800."
            }
          },
          "additionalProperties": false
        }
        """;

    public override async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        try
        {
            var handle = args.ValueKind == JsonValueKind.Object
                         && args.TryGetProperty("hwnd", out var h)
                         && h.ValueKind == JsonValueKind.Number
                ? h.GetInt64()
                : (long?)null;

            var maxDimension = GetNumber(args, "maxDimension") is { } d
                ? (int)d
                : ImageEncoder.DefaultMaxDimension;

            var format = GetString(args, "format") ?? "png";
            if (format is not ("png" or "jpeg"))
            {
                return ToolResult.Error("invalid_argument", "'format' must be 'png' or 'jpeg'.");
            }

            using var bitmap = handle is null
                ? await capture.CaptureMonitorAsync(ct).ConfigureAwait(false)
                : await capture.CaptureWindowAsync(handle.Value, ct).ConfigureAwait(false);

            var encoded = await ImageEncoder
                .EncodeAsync(bitmap, maxDimension, format == "jpeg", ct)
                .ConfigureAwait(false);

            if (encoded.Bytes.Length > MaxBytes)
            {
                return ToolResult.Error("image_too_large",
                    $"The image encoded to {encoded.Bytes.Length / 1024}KB, above the " +
                    $"{MaxBytes / 1024}KB limit. Retry with a lower maxDimension, or " +
                    "format 'jpeg', or use ocr_read_text if you only need the text.");
            }

            var summary = new
            {
                source = handle is null ? "primary monitor" : $"window {handle}",
                captured = new { bitmap.PixelWidth, bitmap.PixelHeight },
                returned = new { encoded.Width, encoded.Height },
                encoded.MimeType,
                bytes = encoded.Bytes.Length,
            };

            // Pixels of someone else's screen are the least fenceable input there is: a
            // banner can precede the image, but nothing wraps the bytes themselves, and an
            // injection rendered as text in a screenshot reads exactly like an instruction.
            // Marked untrusted so it arms the Critical-action block regardless.
            return ToolResult.UntrustedImage(
                summary,
                new ToolImage(
                    Convert.ToBase64String(encoded.Bytes),
                    encoded.MimeType,
                    encoded.Width,
                    encoded.Height),
                CaptureService.TaintSource);
        }
        catch (VisionException ex)
        {
            return ToolResult.Error("capture_unavailable", ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ToolResult.Error("cancelled", "The capture was cancelled.");
        }
        catch (Exception ex)
        {
            return ToolResult.Error("capture_failed", $"Could not capture: {ex.Message}");
        }
    }
}
