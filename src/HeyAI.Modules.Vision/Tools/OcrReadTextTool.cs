using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Modules.Vision.Tools;

public sealed class OcrReadTextTool(CaptureService capture, OcrService ocr) : HeyAITool
{
    public override string Name => "ocr_read_text";
    public override string Title => "Read text from the screen";

    public override string Description =>
        "Captures the screen, or one window, and returns the text found in it using " +
        "Windows' built-in OCR. Use this to read what is currently displayed when the " +
        "content is not otherwise reachable. Pass hwnd from window_list_open to read a " +
        "single window; omit it to read the primary monitor. Read-only.";

    public override ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

    // No window filter here on purpose. Resolving a name to a handle belongs to
    // window_list_open, and duplicating it would make Vision depend on the Window module
    // — the first module-to-module edge in the project. The model already has the handle.
    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "hwnd": {
              "type": "integer",
              "description": "Handle of a window to read, from window_list_open. Omit to read the whole primary monitor."
            },
            "includeLines": {
              "type": "boolean",
              "description": "Also return each line with its position, rather than only the joined text. Defaults to false; positions are large and rarely needed."
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

            using var bitmap = handle is null
                ? await capture.CaptureMonitorAsync(ct).ConfigureAwait(false)
                : await capture.CaptureWindowAsync(handle.Value, ct).ConfigureAwait(false);

            var outcome = await ocr.RecognizeAsync(bitmap, ct).ConfigureAwait(false);

            if (outcome.Lines.Count == 0)
            {
                return ToolResult.Ok(
                    """{"text":"","note":"No text was found. The target may be an image, a video, or blank."}""");
            }

            var includeLines = GetBool(args, "includeLines") ?? false;

            object payload = includeLines
                ? new
                {
                    source = handle is null ? "primary monitor" : $"window {handle}",
                    outcome.Language,
                    outcome.Width,
                    outcome.Height,
                    outcome.Text,
                    outcome.Lines,
                }
                : new
                {
                    source = handle is null ? "primary monitor" : $"window {handle}",
                    outcome.Language,
                    outcome.Text,
                };

            // This is the canonical untrusted input the whole security model was built
            // for: text an attacker put on screen, arriving in the model's context. It
            // arms the read-then-execute block in docs/SECURITY.md.
            return ToolResult.UntrustedJson(payload, OcrService.TaintSource);
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
            return ToolResult.Error("ocr_failed", $"Could not read the screen: {ex.Message}");
        }
    }
}
