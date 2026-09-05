using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using HeyAI.Modules.Vision.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace HeyAI.Modules.Vision;

/// <summary>
/// Grabs a single frame from a monitor or a window using Windows.Graphics.Capture.
///
/// Threading: this does NOT use IWinRtDispatcher, which is worth stating because the
/// dispatcher was built partly on the assumption that it would have to.
/// Direct3D11CaptureFramePool.CreateFreeThreaded exists precisely so a host with no UI
/// thread can capture; FrameArrived then fires on a pool thread. Verified working from
/// an MTA console thread. Only the picker-based API needs a DispatcherQueue, and a
/// headless server cannot show a picker anyway.
/// </summary>
public sealed class CaptureService(Direct3DDevice device)
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>
    /// Screen pixels are whatever happens to be on screen, which is the single most
    /// attacker-controllable input this project has. Anything derived from a capture must
    /// be returned through ToolResult.UntrustedJson.
    /// </summary>
    public const string TaintSource = "screen-capture";

    /// <summary>True when the OS supports capture at all. Always check before trying.</summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch (Exception)
            {
                // Older or stripped-down Windows editions throw rather than return false.
                return false;
            }
        }
    }

    public Task<SoftwareBitmap> CaptureMonitorAsync(CancellationToken ct) =>
        CaptureAsync(interop =>
        {
            var monitor = CaptureNative.MonitorFromWindow(
                CaptureNative.GetDesktopWindow(), CaptureNative.MONITOR_DEFAULTTOPRIMARY);

            var iid = CaptureNative.IGraphicsCaptureItemIid;
            var hr = interop.CreateForMonitor(monitor, in iid, out var item);
            return (hr, item, "primary monitor");
        }, ct);

    public Task<SoftwareBitmap> CaptureWindowAsync(long handle, CancellationToken ct)
    {
        var hwnd = new IntPtr(handle);
        if (!CaptureNative.IsWindow(hwnd))
        {
            throw new VisionException(
                $"No window has handle {handle}. It may have closed; call window_list_open again.");
        }

        return CaptureAsync(interop =>
        {
            var iid = CaptureNative.IGraphicsCaptureItemIid;
            var hr = interop.CreateForWindow(hwnd, in iid, out var item);
            return (hr, item, $"window {handle}");
        }, ct);
    }

    private async Task<SoftwareBitmap> CaptureAsync(
        Func<IGraphicsCaptureItemInterop, (int Hr, IntPtr Item, string What)> create,
        CancellationToken ct)
    {
        if (!IsSupported)
        {
            throw new VisionException("This version of Windows does not support screen capture.");
        }

        var (hr, itemPtr, what) = create(GetInterop());
        if (hr < 0 || itemPtr == IntPtr.Zero)
        {
            throw new VisionException($"Could not open {what} for capture (HRESULT 0x{hr:X8}).");
        }

        GraphicsCaptureItem item;
        try
        {
            item = WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }

        // Two buffers, not one: the pool recycles surfaces, and a single buffer can hand
        // back the frame that is still being written.
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device.Get(), DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);

        using var session = pool.CreateCaptureSession(item);

        try
        {
            // Suppresses the yellow capture border. Best-effort: older builds throw, and a
            // visible border is cosmetic, not a reason to fail the call.
            session.IsBorderRequired = false;
        }
        catch (Exception)
        {
            // Left on.
        }

        var arrived = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        pool.FrameArrived += (sender, _) =>
        {
            var frame = sender.TryGetNextFrame();
            if (frame is not null && !arrived.TrySetResult(frame))
            {
                // Lost the race with an earlier frame; this one is ours to dispose.
                frame.Dispose();
            }
        };

        session.StartCapture();

        // A monitor that is not repainting produces no frame, so this must not hang the
        // transport forever. Ten seconds is generous for one frame.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        Direct3D11CaptureFrame capturedFrame;
        try
        {
            capturedFrame = await arrived.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VisionException(
                $"No frame arrived from {what} within 10 seconds. The target may be minimized " +
                "or not redrawing.");
        }

        using (capturedFrame)
        {
            return await SoftwareBitmap.CreateCopyFromSurfaceAsync(capturedFrame.Surface)
                .AsTask(ct).ConfigureAwait(false);
        }
    }

    private static IGraphicsCaptureItemInterop GetInterop()
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");

        // The factory owns its reference; take our own before wrapping, since the wrapper
        // releases what it is given when it is collected.
        Marshal.AddRef(factory.ThisPtr);
        try
        {
            return (IGraphicsCaptureItemInterop)Wrappers.GetOrCreateObjectForComInstance(
                factory.ThisPtr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(factory.ThisPtr);
        }
    }
}
