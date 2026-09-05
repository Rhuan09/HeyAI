namespace HeyAI.Modules.Window;

/// <summary>
/// One top-level window as reported by the OS.
///
/// Hwnd is a long rather than IntPtr because this crosses into JSON for the model, and
/// IntPtr serialises awkwardly. Note that handles are recycled by Windows, so a handle
/// held across turns may point at a different window by the time it is used.
/// </summary>
public sealed record WindowInfo(long Hwnd, string Title);

/// <summary>
/// Win32 window enumeration.
///
/// Threading: plain user32 calls, MTA-safe. Does NOT use IWinRtDispatcher, for the same
/// reason the Media module does not — see that interface's remarks.
/// </summary>
public sealed class WindowService
{
    /// <summary>
    /// Every top-level window, unfiltered. Expect several hundred entries, most with no
    /// title: the OS counts far more things as windows than a person would.
    /// </summary>
    public IEnumerable<WindowInfo> GetWindows()
    {
        var found = new List<WindowInfo>();

        NativeMethods.WndEnumProc cb = (hwnd, _) =>
        {
            var len = NativeMethods.GetWindowTextLengthW(hwnd);
            Span<char> buf = new char[len + 1];
            var copied = NativeMethods.GetWindowTextW(hwnd, buf, buf.Length);
            found.Add(new WindowInfo(hwnd.ToInt64(), new string(buf[..copied])));
            return 1;
        };

        NativeMethods.EnumWindows(cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return found;
    }
}
