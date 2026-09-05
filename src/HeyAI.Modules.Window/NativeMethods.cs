using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace HeyAI.Modules.Window
{
    internal static partial class NativeMethods
    {
        internal delegate int WndEnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern int EnumWindows(WndEnumProc callback, IntPtr lParam);

        [LibraryImport("user32.dll")]
        internal static partial int GetWindowTextLengthW(IntPtr hWnd);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetWindowTextW(IntPtr hWnd, Span<char> text, int maxCount);

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetForegroundWindow();
    }
}
