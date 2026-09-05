using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Modules.Window.Tools;

public sealed class WindowFocusTool(WindowService windows) : HeyAITool
{
    public override string Name => "window_focus";
    public override string Title => "Focus a window";

    public override string Description =>
        "Brings a window to the foreground, restoring it first if it is minimized. " +
        "Identify the window by hwnd from window_list_open, or by a filter matching its " +
        "title or process name. Windows may refuse to move focus away from whatever the " +
        "user is actively using, and the result says whether it actually took.";

    public override ToolAnnotations Annotations => new() { IdempotentHint = true };

    protected override string SchemaJson =>
        $$"""
        {
          "type": "object",
          "properties": {
            {{WindowTarget.SchemaProperties}}
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Convenience, not Critical: the user takes focus back with one alt-tab, and nothing
    /// leaves the machine. Disruptive while it happens, which is why it ships disabled.
    /// </summary>
    public override RiskLevel EvaluateRisk(JsonElement args) => RiskLevel.Convenience;

    public override Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        try
        {
            var (window, error) = WindowTarget.Resolve(windows, args);
            if (error is not null) return Task.FromResult(error);

            var outcome = windows.Focus(window!.Hwnd);

            // Reported as an error when focus did not move, so the model does not go on to
            // act as though the window is in front. Silent failure is this API's whole
            // hazard; passing it through as success would import it.
            return Task.FromResult(outcome.Succeeded
                ? ToolResult.Json(new
                {
                    ok = true,
                    hwnd = window.Hwnd,
                    process = window.ProcessName,
                    detail = outcome.Detail,
                })
                : ToolResult.Error("focus_refused", outcome.Detail));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error("focus_failed", ex.Message));
        }
    }
}

public sealed class WindowSetStateTool(WindowService windows) : HeyAITool
{
    public override string Name => "window_set_state";
    public override string Title => "Minimize, maximize or restore a window";

    public override string Description =>
        "Minimizes, maximizes or restores a window. Identify the window by hwnd from " +
        "window_list_open, or by a filter matching its title or process name.";

    public override ToolAnnotations Annotations => new() { IdempotentHint = true };

    protected override string SchemaJson =>
        $$"""
        {
          "type": "object",
          "properties": {
            "state": {
              "type": "string",
              "enum": ["minimize", "maximize", "restore"],
              "description": "What the window should become."
            },
            {{WindowTarget.SchemaProperties}}
          },
          "required": ["state"],
          "additionalProperties": false
        }
        """;

    public override RiskLevel EvaluateRisk(JsonElement args) => RiskLevel.Convenience;

    public override Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var state = GetString(args, "state");

        var change = state switch
        {
            "minimize" => WindowStateChange.Minimize,
            "maximize" => WindowStateChange.Maximize,
            "restore" => WindowStateChange.Restore,
            _ => (WindowStateChange?)null,
        };

        if (change is null)
        {
            return Task.FromResult(ToolResult.Error("invalid_argument",
                "'state' must be one of: minimize, maximize, restore."));
        }

        try
        {
            var (window, error) = WindowTarget.Resolve(windows, args);
            if (error is not null) return Task.FromResult(error);

            var outcome = windows.SetState(window!.Hwnd, change.Value);

            return Task.FromResult(outcome.Succeeded
                ? ToolResult.Json(new
                {
                    ok = true,
                    hwnd = window.Hwnd,
                    process = window.ProcessName,
                    state,
                })
                : ToolResult.Error("state_change_refused", outcome.Detail));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error("state_change_failed", ex.Message));
        }
    }
}
