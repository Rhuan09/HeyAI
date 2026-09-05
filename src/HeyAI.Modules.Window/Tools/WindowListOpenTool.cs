using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Modules.Window.Tools;

public sealed class WindowListOpenTool(WindowService windows) : HeyAITool
{
    public override string Name => "window_list_open";
    public override string Title => "List open windows";

    // Written for the model's tool selector, not for a human reading docs. It says what
    // the user would call this ("open windows", "what's running"), and states that hwnd
    // is the identifier other window tools take, since that is the follow-up.
    public override string Description =>
        "Lists the windows currently open on this PC, the same set the user sees when " +
        "alt-tabbing, with the owning process, which window has focus, and which are " +
        "minimized. Use the returned hwnd to identify a window to other window tools. " +
        "Read-only.";

    public override ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "filter": {
              "type": "string",
              "description": "Optional case-insensitive substring matched against both the window title and the process name, e.g. 'firefox' or 'discord'. Omit to list every open window."
            }
          },
          "additionalProperties": false
        }
        """;

    public override Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        try
        {
            IEnumerable<WindowInfo> result = windows.GetOpenWindows();

            var filter = GetString(args, "filter");
            if (!string.IsNullOrWhiteSpace(filter))
            {
                result = result.Where(w =>
                    w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    w.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var list = result.ToList();

            if (list.Count == 0)
            {
                // Distinguishes "nothing matched your filter" from "nothing is open",
                // so the model retries with a broader filter instead of concluding the
                // desktop is empty.
                return Task.FromResult(ToolResult.Ok(filter is null
                    ? """{"windows":[],"note":"No user-facing windows are open."}"""
                    : $$"""{"windows":[],"note":"No open window matched '{{filter}}'. Call again without a filter to see everything."}"""));
            }

            // Window titles are chosen by whatever is running -- a web page sets the
            // browser's title -- so this is attacker-influenceable text arriving in the
            // model's context. See docs/SECURITY.md.
            return Task.FromResult(ToolResult.UntrustedJson(
                new { windows = list }, WindowService.TaintSource));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error("enumeration_failed",
                $"Could not enumerate windows: {ex.Message}"));
        }
    }
}
