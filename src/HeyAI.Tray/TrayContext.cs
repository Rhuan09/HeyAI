using System.Diagnostics;
using HeyAI.Core;
using HeyAI.Core.Confirmation;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace HeyAI.Tray;

/// <summary>
/// The tray icon and its menu.
///
/// Two jobs, and the first matters more than it looks: it is a standing, visible sign
/// that something on this machine can act on the user's behalf. A capability an agent has
/// and a user cannot see is a capability the user has not really consented to.
///
/// The second is giving the permission model a face — the allowlist is a JSON file most
/// people will never open, and a tool nobody can find is a tool nobody turns on.
///
/// It also answers PolicyOutcome.RequireConfirmation for every connected server, over a
/// per-user named pipe. That is what makes Critical mean something other than Deny.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ToolRegistry _registry;
    private readonly HeyAIConfig _config;
    private readonly ConfirmationServer _confirmations;

    /// <summary>
    /// An invisible window used only as a marshalling target. Pipe requests arrive on
    /// thread-pool threads and a dialog has to be created on the UI thread; NotifyIcon is
    /// not a Control, so there is nothing else here to Invoke through.
    /// </summary>
    private readonly Form _uiThread = new();

    public TrayContext()
    {
        // Same composition as the server, so the tray lists exactly the tools the server
        // would register rather than a hand-maintained copy that drifts.
        var provider = new ServiceCollection()
            .AddHeyAICore()
            .AddHeyAIMedia()
            .AddHeyAIWindow()
            .AddHeyAIVision()
            .BuildServiceProvider();

        _registry = provider.GetRequiredService<ToolRegistry>();
        _config = provider.GetRequiredService<HeyAIConfig>();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "HeyAI",
        };

        // Rebuilt on open rather than once at startup: `heyai enable` from the CLI writes
        // the same file, so a menu built at launch would show a stale allowlist.
        _icon.ContextMenuStrip = new ContextMenuStrip();
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();

        _icon.DoubleClick += (_, _) => OpenAuditLog();

        // Forces handle creation. Without a handle Invoke throws, and it would throw the
        // first time a server asked something rather than at startup where it is visible.
        _ = _uiThread.Handle;

        _confirmations = new ConfirmationServer(AskAsync, message =>
            _icon.ShowBalloonTip(4000, "HeyAI", message, ToolTipIcon.Warning));

        UpdateTooltip();
    }

    /// <summary>
    /// Shows the prompt and returns what the person chose. Anything that goes wrong
    /// answers no: a dialog that fails to appear must not become a way to approve things.
    /// </summary>
    private Task<bool> AskAsync(ConfirmationRequest request)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _uiThread.BeginInvoke(() =>
        {
            try
            {
                using var dialog = new ConfirmationDialog(request);
                dialog.ShowDialog();
                completion.SetResult(dialog.Approved);
            }
            catch (Exception)
            {
                completion.SetResult(false);
            }
        });

        return completion.Task;
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        var enabled = _registry.All.Count(t => _config.IsEnabled(t.Name));
        menu.Items.Add(new ToolStripMenuItem($"HeyAI — {enabled} of {_registry.All.Count} tools enabled")
        {
            Enabled = false,
        });

        menu.Items.Add(new ToolStripSeparator());

        foreach (var tool in _registry.All)
        {
            var isEnabled = _config.IsEnabled(tool.Name);

            var item = new ToolStripMenuItem(tool.Title)
            {
                Checked = isEnabled,
                CheckOnClick = false,

                // The name is what appears in config.json and in `heyai enable`, so
                // showing it here is what connects the three.
                ToolTipText = $"{tool.Name}\n{tool.Description}",
            };

            var captured = tool;
            item.Click += (_, _) => Toggle(captured, !isEnabled);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open audit log", null, (_, _) => OpenAuditLog());
        menu.Items.Add("Open state folder", null, (_, _) => OpenFolder(HeyAIPaths.Root));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    private void Toggle(IHeyAITool tool, bool enable)
    {
        var result = _config.SetEnabled(tool.Name, enable);

        if (result == HeyAIConfig.ToggleResult.BlockedByWildcard)
        {
            _icon.ShowBalloonTip(
                4000,
                "HeyAI",
                $"'{tool.Name}' is enabled by a wildcard entry in config.json. Edit it by hand.",
                ToolTipIcon.Warning);
            return;
        }

        if (result == HeyAIConfig.ToggleResult.AlreadyInThatState)
        {
            return;
        }

        _config.Save();
        UpdateTooltip();

        // Servers read config at startup, so a session already connected keeps the old
        // allowlist. Better to say it than to let someone conclude the toggle is broken.
        _icon.ShowBalloonTip(
            4000,
            "HeyAI",
            $"'{tool.Name}' {(enable ? "enabled" : "disabled")}. " +
            "Reconnect the client for it to take effect.",
            ToolTipIcon.Info);
    }

    private void UpdateTooltip()
    {
        var enabled = _registry.All.Count(t => _config.IsEnabled(t.Name));

        // NotifyIcon.Text throws above 63 characters, which is a fun one to discover in
        // production. Nothing here can reach it, but that is why it stays terse.
        _icon.Text = $"HeyAI — {enabled} of {_registry.All.Count} tools enabled";
    }

    private void OpenAuditLog()
    {
        if (!File.Exists(HeyAIPaths.AuditLogFile))
        {
            _icon.ShowBalloonTip(3000, "HeyAI", "No audit entries yet.", ToolTipIcon.Info);
            return;
        }

        // HeyAIPaths.IsProtected forbids a *tool* from exposing this path. The tray is the
        // user's own interface, not an agent surface, and the whole point of an audit log
        // is that the person can read it.
        OpenFolder(HeyAIPaths.AuditLogFile);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(4000, "HeyAI", $"Could not open {path}: {ex.Message}",
                ToolTipIcon.Error);
        }
    }

    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "heyai.ico");
        return File.Exists(path) ? new Icon(path) : SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _confirmations.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _uiThread.Dispose();

            // Without this the icon lingers in the notification area until something else
            // repaints it, which looks like the app failed to close.
            _icon.Visible = false;
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
