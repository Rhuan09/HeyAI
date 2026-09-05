using HeyAI.Core.Confirmation;

namespace HeyAI.Tray;

/// <summary>
/// The prompt a person actually decides at.
///
/// This is a security dialog, so three things are deliberate rather than incidental:
///
/// * Deny is the default and the Escape key, and closing the window denies. The safe
///   answer must be the one you get by doing nothing or by reflex.
/// * Allow is disabled for a moment after the window appears. A prompt that pops up
///   mid-click would otherwise be approved by a click aimed at something else, which is
///   the oldest trick against confirmation dialogs.
/// * Arguments are rendered in a read-only text box, not a label. They can contain text an
///   attacker chose -- a window title, a path lifted off the screen -- and newlines in a
///   label could be used to forge lines that look like the dialog's own words.
/// </summary>
internal sealed class ConfirmationDialog : Form
{
    private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan AutoDeny = TimeSpan.FromSeconds(90);

    private readonly Button _allow;
    private readonly Label _countdown;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly DateTime _shownAt = DateTime.UtcNow;

    public bool Approved { get; private set; }

    public ConfirmationDialog(ConfirmationRequest request)
    {
        Text = "HeyAI — allow this action?";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(520, 340);
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
        };

        layout.Controls.Add(new Label
        {
            Text = $"{request.ToolTitle}",
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        });

        layout.Controls.Add(new Label
        {
            Text = $"{request.ToolName}   ·   risk: {request.Risk}"
                   + (request.Client is null ? string.Empty : $"   ·   asked by: {request.Client}"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 10),
        });

        layout.Controls.Add(new Label
        {
            Text = request.Reason,
            AutoSize = false,
            Height = 46,
            Width = 480,
            Margin = new Padding(0, 0, 0, 8),
        });

        layout.Controls.Add(new Label
        {
            Text = "Arguments (supplied by the caller, shown as data):",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        });

        layout.Controls.Add(new TextBox
        {
            Text = request.ArgumentsJson,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Width = 480,
            Height = 90,
            Font = new Font(FontFamily.GenericMonospace, 8.5f),
            BackColor = SystemColors.Control,
            Margin = new Padding(0, 2, 0, 10),
        });

        _allow = new Button
        {
            Text = "Allow once",
            Width = 130,
            Enabled = false,
        };
        _allow.Click += (_, _) => Close(approved: true);

        var deny = new Button { Text = "Deny", Width = 130 };
        deny.Click += (_, _) => Close(approved: false);

        _countdown = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 6, 12, 0),
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Height = 40,
        };
        buttons.Controls.Add(_allow);
        buttons.Controls.Add(deny);
        buttons.Controls.Add(_countdown);

        layout.Controls.Add(buttons);
        Controls.Add(layout);

        // Escape denies, and so does the window's close button.
        CancelButton = deny;
        AcceptButton = deny;

        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _shownAt;

        if (!_allow.Enabled && elapsed >= ArmDelay)
        {
            _allow.Enabled = true;
        }

        var remaining = AutoDeny - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            // Matches the asking side's timeout. An unattended machine releases the call
            // instead of leaving a dialog nobody will ever answer.
            Close(approved: false);
            return;
        }

        _countdown.Text = $"denies in {remaining.TotalSeconds:F0}s";
    }

    private void Close(bool approved)
    {
        Approved = approved;
        _timer.Stop();
        DialogResult = approved ? DialogResult.OK : DialogResult.Cancel;
        base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
