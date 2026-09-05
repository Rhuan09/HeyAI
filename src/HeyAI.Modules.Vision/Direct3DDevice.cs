using System.Runtime.InteropServices;
using HeyAI.Modules.Vision.Interop;
using Windows.Graphics.DirectX.Direct3D11;

namespace HeyAI.Modules.Vision;

public sealed class VisionException(string message) : Exception(message);

/// <summary>
/// Owns the one Direct3D device the capture pipeline needs.
///
/// Windows.Graphics.Capture will not hand out frames without an IDirect3DDevice, and
/// getting one is a three-step climb the projection does not expose: create an
/// ID3D11Device, query it for IDXGIDevice, then hand that to
/// CreateDirect3D11DeviceFromDXGIDevice, which returns an IInspectable to marshal back
/// into the WinRT type.
///
/// Created once and shared. Device creation costs tens of milliseconds and holds GPU
/// resources, so doing it per capture would make every OCR call pay for it.
///
/// Threading: an ID3D11Device is free-threaded, which is what makes sharing it safe now
/// that the transport dispatches tool calls concurrently. The immediate context is NOT,
/// and is deliberately never used here — the capture path only needs the device.
/// </summary>
public sealed class Direct3DDevice : IDisposable
{
    private readonly Lock _gate = new();
    private IDirect3DDevice? _device;
    private IntPtr _d3dDevice;
    private IntPtr _dxgiDevice;
    private IntPtr _context;
    private bool _disposed;

    public IDirect3DDevice Get()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _device ??= Create();
        }
    }

    private IDirect3DDevice Create()
    {
        // WARP is the fallback: a machine with no usable GPU driver, or an RDP session,
        // still has to be able to read the screen. Slower, not absent.
        var hr = CaptureNative.D3D11CreateDevice(
            IntPtr.Zero, CaptureNative.D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
            CaptureNative.D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0,
            CaptureNative.D3D11_SDK_VERSION, out _d3dDevice, out _, out _context);

        if (hr < 0)
        {
            hr = CaptureNative.D3D11CreateDevice(
                IntPtr.Zero, CaptureNative.D3D_DRIVER_TYPE_WARP, IntPtr.Zero,
                CaptureNative.D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0,
                CaptureNative.D3D11_SDK_VERSION, out _d3dDevice, out _, out _context);
        }

        if (hr < 0)
        {
            throw new VisionException(
                $"Could not create a Direct3D device (HRESULT 0x{hr:X8}). " +
                "Screen capture is unavailable on this machine.");
        }

        var iid = CaptureNative.IDXGIDeviceIid;
        hr = Marshal.QueryInterface(_d3dDevice, in iid, out _dxgiDevice);
        if (hr < 0)
        {
            throw new VisionException($"Direct3D device has no IDXGIDevice (HRESULT 0x{hr:X8}).");
        }

        hr = CaptureNative.CreateDirect3D11DeviceFromDXGIDevice(_dxgiDevice, out var inspectable);
        if (hr < 0)
        {
            throw new VisionException(
                $"Could not project the Direct3D device into WinRT (HRESULT 0x{hr:X8}).");
        }

        try
        {
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            (_device as IDisposable)?.Dispose();
            _device = null;

            foreach (ref var handle in new[] { _context, _dxgiDevice, _d3dDevice }.AsSpan())
            {
                if (handle != IntPtr.Zero) Marshal.Release(handle);
            }

            _context = _dxgiDevice = _d3dDevice = IntPtr.Zero;
        }
    }
}
