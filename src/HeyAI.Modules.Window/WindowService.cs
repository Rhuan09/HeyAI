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
