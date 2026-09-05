using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Modules.Window.Tools;

/// <summary>
/// Shared targeting for tools that act on one window.
///
/// Two ways in, on purpose. A handle is precise but requires the model to have called
/// window_list_open first and to still be holding a valid number; a filter matches how a
/// person actually asks ("focus VS Code") and costs no round trip. Supporting only
/// handles would make every action two calls.
/// </summary>
internal static class WindowTarget
{
    internal const string SchemaProperties =
        """
        "hwnd": {
          "type": "integer",
          "description": "Handle from window_list_open. Precise; prefer it when you already have one."
        },
        "filter": {
          "type": "string",
          "description": "Case-insensitive substring matched against window title and process name, e.g. 'firefox'. Use when you do not have a handle. Must match exactly one window."
        }
        """;

    /// <summary>
    /// Resolves the target, or produces the error to return. Exactly one of the two
    /// results is non-null.
    /// </summary>
    internal static (WindowInfo? Window, ToolResult? Error) Resolve(
        WindowService windows, JsonElement args)
    {
        var hasHwnd = args.ValueKind == JsonValueKind.Object
                      && args.TryGetProperty("hwnd", out var hwndElement)
                      && hwndElement.ValueKind == JsonValueKind.Number;

        var filter = args.ValueKind == JsonValueKind.Object
                     && args.TryGetProperty("filter", out var f)
                     && f.ValueKind == JsonValueKind.String
            ? f.GetString()
            : null;

        if (hasHwnd)
        {
            args.TryGetProperty("hwnd", out var element);
            var handle = element.GetInt64();

            // Re-resolved through the live list rather than trusted as a number. Windows
            // recycles handles, so one held across turns can name a different window --
            // acting on it directly would be a use-after-free with a UI.
            var window = windows.FindByHandle(handle);

            return window is not null
                ? (window, null)
                : (null, ToolResult.Error("window_not_found",
                    $"No open window has handle {handle}. It may have closed. " +
                    "Call window_list_open again for current handles."));
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return (null, ToolResult.Error("invalid_argument",
                "Provide either 'hwnd' or 'filter' to say which window to act on."));
        }

        var matches = windows.FindByFilter(filter);

        return matches.Count switch
        {
            1 => (matches[0], null),

            0 => (null, ToolResult.Error("no_match",
                $"No open window matched '{filter}'. Call window_list_open to see what is open.")),

            // The candidate list carries window titles, which are attacker-influenceable,
            // so this error is fenced like any other untrusted output. An error message is
            // a particularly attractive place to hide an injection, because it is where
            // the model looks for what to do next.
            _ => (null, ToolResult.UntrustedError("ambiguous_match",
                $"'{filter}' matched {matches.Count} windows. Re-issue with one of these hwnd values:\n" +
                string.Join("\n", matches.Select(m => $"  {m.Hwnd}  {m.ProcessName}  {m.Title}")),
                WindowService.TaintSource)),
        };
    }
}
