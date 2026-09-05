namespace HeyAI.Core.Threading;

/// <summary>
/// Marshals work onto a thread that owns an STA apartment and a pumped DispatcherQueue.
///
/// WHEN YOU NEED THIS: any WinRT type that is apartment-affine or requires a
/// DispatcherQueue on the calling thread. Concretely --
///   * toast activation callbacks
///   * anything handing you a XAML or composition object
///   * GraphicsCapturePicker, which a headless server cannot show anyway
///
/// NOT Windows.Graphics.Capture in general. An earlier version of this comment claimed
/// it was, which is wrong: Direct3D11CaptureFramePool.CreateFreeThreaded exists precisely
/// so a host with no UI thread can capture, and HeyAI.Modules.Vision uses it from MTA.
/// Only the picker-based entry point needs a DispatcherQueue.
///
/// WHEN YOU DO NOT: plain COM/Win32 (Core Audio, User32) and GSMTC are MTA-safe and must
/// NOT be routed here. Forcing them onto a single pumped thread serialises every call and
/// invites re-entrancy deadlocks. The Media module deliberately bypasses this.
///
/// A console app's main thread is MTA. Calling a DispatcherQueue-affine API from it fails
/// at runtime, usually as RPC_E_WRONG_THREAD or a silent hang. That is the single most
/// common way a project shaped like this loses a week.
/// </summary>
public interface IWinRtDispatcher : IAsyncDisposable
{
    /// <summary>True once the queue thread is running and pumping messages.</summary>
    bool IsRunning { get; }

    Task<T> InvokeAsync<T>(Func<T> work, CancellationToken ct = default);

    Task InvokeAsync(Action work, CancellationToken ct = default);

    /// <summary>For WinRT async operations that must start and complete on the queue thread.</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}
