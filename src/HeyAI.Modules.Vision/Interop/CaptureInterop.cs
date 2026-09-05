using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HeyAI.Modules.Vision.Interop;

/// <summary>
/// The activation-factory interface that turns an HWND or HMONITOR into a
/// GraphicsCaptureItem.
///
/// It exists because the WinRT surface only offers a user-facing picker dialog, which a
/// headless server cannot show. This is the documented way for a desktop app to capture
/// something it already identified.
/// </summary>
[GeneratedComInterface]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
internal partial interface IGraphicsCaptureItemInterop
{
    [PreserveSig] int CreateForWindow(IntPtr hwnd, in Guid iid, out IntPtr result);
    [PreserveSig] int CreateForMonitor(IntPtr hmonitor, in Guid iid, out IntPtr result);
}

internal static partial class CaptureNative
{
    /// <summary>
    /// IID of IGraphicsCaptureItem, the WinRT interface.
    ///
    /// Hard-coded on purpose: typeof(GraphicsCaptureItem).GUID returns the GUID of the
    /// projected .NET type, which is a different value, and passing it makes
    /// CreateForMonitor fail with E_NOINTERFACE for no visible reason.
    /// </summary>
    internal static readonly Guid IGraphicsCaptureItemIid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    internal static readonly Guid IDXGIDeviceIid =
        new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    internal const int D3D_DRIVER_TYPE_HARDWARE = 1;
    internal const int D3D_DRIVER_TYPE_WARP = 5;
    internal const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    internal const uint D3D11_SDK_VERSION = 7;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 1;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [LibraryImport("d3d11.dll")]
    internal static partial int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [LibraryImport("d3d11.dll")]
    internal static partial int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetDesktopWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hwnd);
}
