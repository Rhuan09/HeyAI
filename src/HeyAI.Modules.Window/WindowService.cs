using System.Diagnostics;

namespace HeyAI.Modules.Window;

/// <summary>
/// One top-level window, as a person would recognise it.
///
/// Hwnd is a long rather than IntPtr because this crosses into JSON for the model, and
/// IntPtr serialises awkwardly. Windows recycles handles, so one held across turns can
/// point at a different window by the time it is used — tools that act on a handle must
/// re-validate it against ProcessId and Title first.
/// </summary>
public sealed record WindowInfo
{
    public required long Hwnd { get; init; }
    public required string Title { get; init; }
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required bool IsForeground { get; init; }
    public required bool IsMinimized { get; init; }
}

/// <summary>What a window can be asked to become.</summary>
public enum WindowStateChange
{
    Minimize,
    Maximize,
    Restore,
}

/// <summary>
/// Result of an action that the OS may refuse without saying so.
/// </summary>
public sealed record WindowActionOutcome(bool Succeeded, string Detail);

/// <summary>
/// Win32 window enumeration, filtered to what a person means by "my open windows".
///
/// Threading: plain user32 and dwmapi calls, MTA-safe. Does NOT use IWinRtDispatcher, for
/// the same reason the Media module does not — see that interface's remarks.
/// </summary>
public sealed class WindowService
{
    /// <summary>
    /// Window titles are chosen by whatever is running: a web page sets the browser's
    /// title, so this is attacker-influenceable text. Callers must return it through
    /// ToolResult.UntrustedJson.
    /// </summary>
    public const string TaintSource = "win32-window-titles";

    /// <summary>
    /// Every top-level window the OS knows about, with no filtering at all. Expect several
    /// hundred, most with no title. Exposed because <see cref="GetOpenWindows"/> is a
    /// heuristic, and diagnosing "why is my window missing" starts here.
    /// </summary>
    public IReadOnlyList<WindowInfo> GetAllWindows() => Enumerate(w => true);

    /// <summary>
    /// The alt-tab list, approximately. Five layers, cheapest and most selective first —
    /// on a normal desktop IsWindowVisible alone removes over 90%:
    ///
    ///   1. visible                 419 -> 29   (measured on one desktop)
    ///   2. has a title              29 -> 18
    ///   3. has no owner             18 -> 18   removes modal dialogs; often a no-op
    ///   4. is not a tool window     18 -> 16   palettes and toolbars, which alt-tab also hides
    ///   5. is not DWM-cloaked       16 -> 12   suspended UWP apps and other virtual desktops
    ///
    /// Layer 3 is the least proven and the most likely source of a false negative: some
    /// legitimate windows are owned, and the real alt-tab rule involves the last active
    /// popup rather than a flat owner check. Suspect it first if a window goes missing.
    /// </summary>
    public IReadOnlyList<WindowInfo> GetOpenWindows() => Enumerate(IsUserFacing);

    /// <summary>
    /// Resolves a handle the model supplied to a window that exists right now.
    ///
    /// Windows recycles handles, so one held across turns can name a different window --
    /// or none. Every action re-resolves through the live list rather than trusting the
    /// number, which closes that gap: a recycled handle either is not user-facing or
    /// comes back with a process and title the caller can see changed.
    /// </summary>
    public WindowInfo? FindByHandle(long handle) =>
        GetOpenWindows().FirstOrDefault(w => w.Hwnd == handle);

    /// <summary>Case-insensitive substring over title and process name.</summary>
    public IReadOnlyList<WindowInfo> FindByFilter(string filter) =>
        GetOpenWindows()
            .Where(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || w.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Brings a window to the foreground, and reports honestly whether it worked.
    ///
    /// SetForegroundWindow returns true far more often than it moves focus. Windows'
    /// foreground lock blocks any process that is not already foreground -- which a
    /// background MCP server never is -- and the call then just flashes the taskbar
    /// button while reporting success.
    ///
    /// The way through is AttachThreadInput: joining our input queue to the current
    /// foreground window's thread makes the OS treat us as part of that input context.
    /// It is the long-standing workaround, not a guarantee, so the result is verified
    /// against GetForegroundWindow rather than against the return value.
    /// </summary>
    public WindowActionOutcome Focus(long handle)
    {
        var hwnd = new IntPtr(handle);
        if (!NativeMethods.IsWindow(hwnd))
        {
            return new WindowActionOutcome(false, "that window no longer exists");
        }

        // A minimized window cannot take focus. Restore first, or the foreground call
        // succeeds against a window nobody can see.
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return new WindowActionOutcome(true, "already in the foreground");
        }

        var ourThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);

        var attached = foregroundThread != 0
                       && foregroundThread != ourThread
                       && NativeMethods.AttachThreadInput(ourThread, foregroundThread, true);

        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            // Leaving input queues attached would couple this process to another app's
            // input for the rest of its life. Detach even if the call above threw.
            if (attached)
            {
                NativeMethods.AttachThreadInput(ourThread, foregroundThread, false);
            }
        }

        return NativeMethods.GetForegroundWindow() == hwnd
            ? new WindowActionOutcome(true, "focused")
            : new WindowActionOutcome(false,
                "the OS foreground lock refused the change; the window's taskbar button " +
                "is probably flashing instead");
    }

    /// <summary>
    /// Minimizes, maximizes or restores. ShowWindow's return value reports the window's
    /// previous visibility, not success, so the new state is read back instead.
    /// </summary>
    public WindowActionOutcome SetState(long handle, WindowStateChange change)
    {
        var hwnd = new IntPtr(handle);
        if (!NativeMethods.IsWindow(hwnd))
        {
            return new WindowActionOutcome(false, "that window no longer exists");
        }

        var command = change switch
        {
            WindowStateChange.Minimize => NativeMethods.SW_MINIMIZE,
            WindowStateChange.Maximize => NativeMethods.SW_MAXIMIZE,
            WindowStateChange.Restore => NativeMethods.SW_RESTORE,
            _ => NativeMethods.SW_RESTORE,
        };

        NativeMethods.ShowWindow(hwnd, command);

        var minimized = NativeMethods.IsIconic(hwnd);
        var expectedMinimized = change == WindowStateChange.Minimize;

        return minimized == expectedMinimized
            ? new WindowActionOutcome(true, change.ToString().ToLowerInvariant() + "d")
            : new WindowActionOutcome(false, $"the window did not accept {change}");
    }

    private static bool IsUserFacing(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetWindowTextLengthW(hwnd) == 0) return false;
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return false;

        var exStyle = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        return !IsCloaked(hwnd);
    }

    /// <summary>
    /// DWM cloaking is what hides a suspended UWP app or a window on another virtual
    /// desktop while IsWindowVisible still reports true.
    ///
    /// A failed call is treated as not cloaked. The window may have died between
    /// enumeration and this check, and showing one window too many is a smaller harm than
    /// hiding the one the user is looking at.
    /// </summary>
    private static bool IsCloaked(IntPtr hwnd)
    {
        var hr = NativeMethods.DwmGetWindowAttribute(
            hwnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int));

        return hr >= 0 && cloaked != 0;
    }

    private static IReadOnlyList<WindowInfo> Enumerate(Func<IntPtr, bool> predicate)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var found = new List<WindowInfo>();

        NativeMethods.WndEnumProc callback = (hwnd, _) =>
        {
            // An exception escaping here crosses a native frame and kills the process
            // outright — no stack trace, and ToolInvoker's catch is below this, so it
            // cannot help. Everything in the body must be non-throwing.
            try
            {
                if (predicate(hwnd)) found.Add(Describe(hwnd, foreground));
            }
            catch (Exception)
            {
                // A window can be destroyed mid-enumeration. Skip it.
            }

            return 1; // 0 would stop the enumeration early.
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    private static WindowInfo Describe(IntPtr hwnd, IntPtr foreground)
    {
        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        Span<char> buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowTextW(hwnd, buffer, buffer.Length);

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);

        return new WindowInfo
        {
            Hwnd = hwnd.ToInt64(),
            Title = new string(buffer[..copied]),
            ProcessId = processId,
            ProcessName = ProcessNameOf(processId),
            IsForeground = hwnd == foreground,
            IsMinimized = NativeMethods.IsIconic(hwnd),
        };
    }

    private static string ProcessNameOf(uint processId)
    {
        if (processId == 0) return "system";
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // The process can exit between enumeration and lookup; not an error.
            return $"pid:{processId}";
        }
    }
}
