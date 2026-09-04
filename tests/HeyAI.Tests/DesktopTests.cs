using HeyAI.Core.Threading;
using HeyAI.Modules.Media.Audio;
using HeyAI.Modules.Media.Gsmtc;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// Integration tests that need a real interactive desktop session: an audio endpoint, a
/// window station, a running media app.
///
/// GitHub Actions windows-latest has no audio device and no media session, so these are
/// excluded from CI:
///     dotnet test --filter "Category!=RequiresDesktop"
/// and run locally with:
///     dotnet test --filter "Category=RequiresDesktop"
///
/// Contributors are expected to run both before opening a PR. A module whose only tests
/// need a desktop is a module nobody can review, so keep the logic testable off-desktop
/// and let these cover the interop boundary only.
/// </summary>
[Trait("Category", "RequiresDesktop")]
public sealed class DesktopTests
{
    [Fact]
    public async Task Dispatcher_runs_work_on_an_sta_thread()
    {
        await using var dispatcher = new StaWinRtDispatcher();
        await dispatcher.WaitForReadyAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var apartment = await dispatcher.InvokeAsync(() => Thread.CurrentThread.GetApartmentState(), TestContext.Current.CancellationToken);
        var threadName = await dispatcher.InvokeAsync(() => Thread.CurrentThread.Name, TestContext.Current.CancellationToken);

        Assert.Equal(ApartmentState.STA, apartment);
        Assert.Equal("HeyAI.WinRT.STA", threadName);
    }

    [Fact]
    public async Task Dispatcher_marshals_every_call_to_the_same_thread()
    {
        await using var dispatcher = new StaWinRtDispatcher();
        await dispatcher.WaitForReadyAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var ids = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => dispatcher.InvokeAsync(() => Environment.CurrentManagedThreadId, TestContext.Current.CancellationToken)));

        Assert.Single(ids.Distinct());
    }

    [Fact]
    public void Enumerates_at_least_one_audio_render_device()
    {
        var devices = new AudioService().GetRenderDevices();

        Assert.NotEmpty(devices);
        Assert.Contains(devices, d => d.IsDefault);
        Assert.All(devices, d => Assert.InRange(d.Volume, 0f, 1f));
    }

    [Fact]
    public void Master_volume_round_trips()
    {
        var audio = new AudioService();
        var original = audio.GetRenderDevices().Single(d => d.IsDefault).Volume;

        try
        {
            var (level, _) = audio.SetMasterVolume(0.42f, mute: null);
            Assert.Equal(0.42f, level, tolerance: 0.02f);
        }
        finally
        {
            audio.SetMasterVolume(original, mute: null);
        }
    }

    [Fact]
    public async Task Reading_media_sessions_does_not_throw_without_a_player()
    {
        // GSMTC must degrade to an empty list rather than faulting when nothing is playing.
        var sessions = await new MediaSessionService().GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(sessions);
    }
}
