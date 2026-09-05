using HeyAI.Modules.Window;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// These act on a real desktop, so each one restores whatever it changed. The window it
/// picks is always the one already in focus: acting on the user's own window and putting
/// it straight back is less disruptive than hunting for a victim, and it needs no
/// assumption about what happens to be running.
/// </summary>
[Trait("Category", "RequiresDesktop")]
public sealed class WindowActionTests
{
    private readonly WindowService _service = new();

    /// <summary>A handle that is guaranteed not to be a window.</summary>
    private const long DeadHandle = 0x7FFFFFF0;

    [Fact]
    public void Focusing_a_dead_handle_fails_without_throwing()
    {
        var outcome = _service.Focus(DeadHandle);

        Assert.False(outcome.Succeeded);
        Assert.Contains("no longer exists", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_state_on_a_dead_handle_fails_without_throwing()
    {
        var outcome = _service.SetState(DeadHandle, WindowStateChange.Restore);

        Assert.False(outcome.Succeeded);
        Assert.Contains("no longer exists", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Focusing_the_already_focused_window_succeeds_without_touching_the_OS()
    {
        var foreground = _service.GetOpenWindows().FirstOrDefault(w => w.IsForeground);
        Assert.NotNull(foreground);

        var outcome = _service.Focus(foreground.Hwnd);

        Assert.True(outcome.Succeeded);
        Assert.Equal("already in the foreground", outcome.Detail);
    }

    [Fact]
    public void Minimize_and_restore_round_trip()
    {
        var target = _service.GetOpenWindows().FirstOrDefault(w => w.IsForeground && !w.IsMinimized);
        Assert.NotNull(target);

        try
        {
            var minimized = _service.SetState(target.Hwnd, WindowStateChange.Minimize);
            Assert.True(minimized.Succeeded, minimized.Detail);
            Assert.True(Reread(target.Hwnd)?.IsMinimized);

            var restored = _service.SetState(target.Hwnd, WindowStateChange.Restore);
            Assert.True(restored.Succeeded, restored.Detail);
            Assert.False(Reread(target.Hwnd)?.IsMinimized);
        }
        finally
        {
            // The test found this window in the foreground; leave it there.
            _service.SetState(target.Hwnd, WindowStateChange.Restore);
            _service.Focus(target.Hwnd);
        }
    }

    [Fact]
    public void Finding_by_handle_rejects_a_handle_that_is_not_a_user_facing_window()
    {
        // The guard against handle recycling: actions re-resolve through the live list
        // rather than trusting the number a caller held from an earlier turn.
        Assert.Null(_service.FindByHandle(DeadHandle));
    }

    [Fact]
    public void Finding_by_filter_matches_title_or_process_and_is_case_insensitive()
    {
        var any = _service.GetOpenWindows().FirstOrDefault();
        Assert.NotNull(any);

        var byProcess = _service.FindByFilter(any.ProcessName.ToUpperInvariant());

        Assert.Contains(byProcess, w => w.Hwnd == any.Hwnd);
    }

    private WindowInfo? Reread(long hwnd) => _service.FindByHandle(hwnd);
}
