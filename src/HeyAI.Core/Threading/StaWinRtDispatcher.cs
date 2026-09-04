using System.Runtime.InteropServices;
using Windows.System;

namespace HeyAI.Core.Threading;

/// <summary>
/// Owns one dedicated STA thread that runs a Win32 message loop and hosts a
/// DispatcherQueue created through CoreMessaging's CreateDispatcherQueueController.
///
/// This is the unpackaged/console equivalent of what WinUI sets up for you. The
/// controller is created with DQTAT_COM_NONE because the CLR has already put this thread
/// into an STA, and DQTYPE_THREAD_CURRENT so the queue binds here rather than spawning
/// a second thread.
/// </summary>
public sealed partial class StaWinRtDispatcher : IWinRtDispatcher
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DispatcherQueueController? _controller;
    private DispatcherQueue? _queue;
    private uint _nativeThreadId;
    private volatile bool _disposed;

    public StaWinRtDispatcher()
    {
        _thread = new Thread(ThreadMain, maxStackSize: 1024 * 1024)
        {
            IsBackground = true,
            Name = "HeyAI.WinRT.STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public bool IsRunning => _ready.Task.IsCompletedSuccessfully && !_disposed;

    /// <summary>Completes once the queue thread is pumping. Await once during host startup.</summary>
    public Task WaitForReadyAsync() => _ready.Task;

    private void ThreadMain()
    {
        try
        {
            _nativeThreadId = GetCurrentThreadId();

            var options = new DispatcherQueueOptions
            {
                DwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                ThreadType = DQTYPE_THREAD_CURRENT,
                ApartmentType = DQTAT_COM_NONE,
            };

            var hr = CreateDispatcherQueueController(options, out var abi);
            if (hr < 0)
            {
                _ready.TrySetException(Marshal.GetExceptionForHR(hr)!);
                return;
            }

            _controller = WinRT.MarshalInterface<DispatcherQueueController>.FromAbi(abi);
            Marshal.Release(abi);
            _queue = _controller.DispatcherQueue;
            _ready.TrySetResult();

            // The DispatcherQueue drains on window messages, so this loop *is* the queue.
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(in msg);
                DispatchMessageW(in msg);
            }
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken ct = default) =>
        EnqueueAsync(() => Task.FromResult(work()), ct);

    public Task InvokeAsync(Action work, CancellationToken ct = default) =>
        EnqueueAsync<object?>(() => { work(); return Task.FromResult<object?>(null); }, ct);

    public Task<T> InvokeAsync<T>(Func<Task<T>> work, CancellationToken ct = default) =>
        EnqueueAsync(work, ct);

    private async Task<T> EnqueueAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _ready.Task.ConfigureAwait(false);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = ct.Register(() => tcs.TrySetCanceled(ct)).ConfigureAwait(false);

        var enqueued = _queue!.TryEnqueue(async void () =>
        {
            try
            {
                // ConfigureAwait(true): continuations must stay on the queue thread,
                // otherwise a WinRT object created here gets touched from the pool.
                tcs.TrySetResult(await work().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            throw new InvalidOperationException("WinRT dispatcher queue rejected the work item.");
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_controller is not null && _ready.Task.IsCompletedSuccessfully)
        {
            try
            {
                await _controller.ShutdownQueueAsync();
            }
            catch (Exception)
            {
                // Shutting down; a failure here must not mask the real exit path.
            }
        }

        if (_nativeThreadId != 0)
        {
            PostThreadMessageW(_nativeThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
    }

    // --- interop ---------------------------------------------------------------

    private const int DQTYPE_THREAD_CURRENT = 2;
    private const int DQTAT_COM_NONE = 0;
    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int DwSize;
        public int ThreadType;
        public int ApartmentType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(
        DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
