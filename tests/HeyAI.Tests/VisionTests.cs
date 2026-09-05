using HeyAI.Modules.Vision;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// Capture and OCR against the real screen. Needs a desktop with a composited display, so
/// excluded from CI — GitHub's runners have no interactive session.
/// </summary>
[Trait("Category", "RequiresDesktop")]
public sealed class VisionTests : IDisposable
{
    private readonly Direct3DDevice _device = new();

    [Fact]
    public void Capture_is_supported_on_this_machine()
    {
        Assert.True(CaptureService.IsSupported);
    }

    [Fact]
    public void The_direct3d_device_is_created_once_and_reused()
    {
        // Shared because creation costs tens of milliseconds; safe to share because an
        // ID3D11Device is free-threaded, which matters now that tools run concurrently.
        Assert.Same(_device.Get(), _device.Get());
    }

    [Fact]
    public async Task Capturing_the_primary_monitor_yields_a_bitmap()
    {
        using var bitmap = await new CaptureService(_device)
            .CaptureMonitorAsync(TestContext.Current.CancellationToken);

        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
    }

    [Fact]
    public async Task Capturing_a_handle_that_is_not_a_window_fails_cleanly()
    {
        var capture = new CaptureService(_device);

        var ex = await Assert.ThrowsAsync<VisionException>(
            () => capture.CaptureWindowAsync(0x7FFFFFF0, TestContext.Current.CancellationToken));

        Assert.Contains("No window has handle", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_runs_over_a_captured_screen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var bitmap = await new CaptureService(_device).CaptureMonitorAsync(ct);

        var outcome = await new OcrService().RecognizeAsync(bitmap, ct);

        // What the screen says is not assertable — the desktop is whatever it is. The
        // invariants are that an engine existed, it reported its language, and the
        // dimensions came back matching the bitmap.
        Assert.NotEmpty(outcome.Language);
        Assert.Equal(bitmap.PixelWidth, outcome.Width);
        Assert.Equal(bitmap.PixelHeight, outcome.Height);
        Assert.NotNull(outcome.Text);
    }

    [Fact]
    public async Task Capture_can_be_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CaptureService(_device).CaptureMonitorAsync(cts.Token));
    }

    public void Dispose() => _device.Dispose();
}
