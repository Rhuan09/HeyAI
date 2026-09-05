using System.Runtime.InteropServices;

namespace HeyAI.Modules.Window;

internal static partial class NativeMethods
{
    internal const uint GW_OWNER = 4;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const int DWMWA_CLOAKED = 14;

    /// <summary>
    /// EnumWindows hands the OS a function pointer and calls it once per window, so this
    /// is the one place the CONTRIBUTING rule "LibraryImport, not DllImport" is broken:
    /// the LibraryImport source generator does not marshal delegates, and the alternative
    /// (an [UnmanagedCallersOnly] static plus GCHandle state through lParam) buys nothing
    /// here.
    ///
    /// The usual delegate-lifetime hazard does not apply: EnumWindows is synchronous, so
    /// the callback only fires while the call is on the stack. That hazard is for
    /// callbacks the OS retains, such as window procedures.
    /// </summary>
    internal delegate int WndEnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern int EnumWindows(WndEnumProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowTextLengthW(IntPtr hWnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetWindowTextW(IntPtr hWnd, Span<char> text, int maxCount);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    // Win32 BOOL is four bytes; C# bool is one. The marshalling must be stated or the
    // return value is read from the wrong width.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetWindow(IntPtr hWnd, uint command);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Not user32 — the Desktop Window Manager owns cloaking. Returns an HRESULT, and
    /// callers must treat failure as "not cloaked" rather than propagating it.
    /// </summary>
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(
        IntPtr hWnd, int attribute, out int value, int size);
}
