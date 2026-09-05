using System.Diagnostics;
using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Modules.Shell.Tools;

public sealed class ShellOpenPathTool : HeyAITool
{
    public override string Name => "shell_open_path";
    public override string Title => "Open a file or folder";

    public override string Description =>
        "Opens a file or folder the way double-clicking it would, or reveals it in " +
        "Explorer without opening it. Use mode 'reveal' when the user wants to find " +
        "something rather than launch it — it never runs an application and needs less " +
        "permission. Opening a file starts whatever program is registered for it.";

    // openWorldHint: opening a file hands control to a program HeyAI did not write and
    // cannot constrain. destructiveHint stays false because opening destroys nothing, but
    // that says nothing about what the launched program then does.
    public override ToolAnnotations Annotations => new() { OpenWorldHint = true };

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Absolute path to a file or folder. Not a URL."
            },
            "mode": {
              "type": "string",
              "enum": ["open", "reveal"],
              "description": "'open' launches the registered program for the item. 'reveal' shows it selected in Explorer and never launches anything. Defaults to 'open'."
            }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Classified from the mode alone, and deliberately not from the file extension.
    ///
    /// Guessing what will happen from a string is the trap here. A path with no extension
    /// may be a file; an extension may not match the registered handler; and the target
    /// can change between this call and execution, so anything learned from the filesystem
    /// would be a time-of-check answer to a time-of-use question.
    ///
    /// So: opening anything is Critical, because a program runs and HeyAI cannot say
    /// which. Revealing is Convenience, because it always does exactly one thing —
    /// Explorer, with the item selected.
    ///
    /// The cost is that opening a Documents folder needs approval too. That is the right
    /// side to err on, and 'reveal' is the cheap path for "where is this".
    /// </summary>
    public override RiskLevel EvaluateRisk(JsonElement args) =>
        GetString(args, "mode") == "reveal" ? RiskLevel.Convenience : RiskLevel.Critical;

    public override Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var mode = GetString(args, "mode") ?? "open";
        if (mode is not ("open" or "reveal"))
        {
            return Task.FromResult(ToolResult.Error("invalid_argument",
                "'mode' must be 'open' or 'reveal'."));
        }

        var verdict = ShellPathGuard.Check(GetString(args, "path"));
        if (!verdict.Allowed)
        {
            return Task.FromResult(ToolResult.Error("path_refused", verdict.Reason!));
        }

        var full = verdict.FullPath!;

        try
        {
            if (mode == "reveal")
            {
                // /select, takes the containing folder and highlights the item. The comma
                // with no space is the documented form and explorer ignores the argument
                // if it is written any other way.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"")
                {
                    UseShellExecute = true,
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
            }

            return Task.FromResult(ToolResult.Json(new
            {
                ok = true,
                mode,
                path = full,
                kind = Directory.Exists(full) ? "folder" : "file",
            }));
        }
        catch (Exception ex)
        {
            // A missing handler, a policy block, a user cancelling the "open with" dialog.
            return Task.FromResult(ToolResult.Error("open_failed",
                $"Could not open {full}: {ex.Message}"));
        }
    }
}
