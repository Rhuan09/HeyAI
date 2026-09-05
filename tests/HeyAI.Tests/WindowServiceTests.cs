using HeyAI.Modules.Window;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// Window enumeration cannot be tested hermetically — it reports whatever is on the
/// machine at that instant. So every assertion here is an invariant that holds on any
/// desktop, never a specific window. "Firefox is open" would be a test of the tester's
/// machine; "the window in focus is listed" is a test of the code.
/// </summary>
[Trait("Category", "RequiresDesktop")]
public sealed class WindowServiceTests
{
    private readonly WindowService _service = new();

    [Fact]
    public void The_foreground_window_is_never_filtered_out()
    {
        // The real failure mode of a five-layer filter is being too aggressive, not too
        // permissive. If the window the user is looking at disappears, this catches it.
        var foreground = NativeMethods.GetForegroundWindow().ToInt64();
        Assert.NotEqual(0, foreground);

        var windows = _service.GetOpenWindows();

        Assert.Contains(windows, w => w.Hwnd == foreground);
        Assert.Contains(windows, w => w.IsForeground);
    }

    [Fact]
    public void Filtering_removes_the_overwhelming_majority_of_windows()
    {
        var all = _service.GetAllWindows();
        var open = _service.GetOpenWindows();

        Assert.True(all.Count > open.Count,
            $"filter removed nothing: {all.Count} raw vs {open.Count} filtered");

        // Measured at 419 raw against 12 filtered on a normal desktop. A ceiling rather
        // than an exact figure, because the count moves with what the user has open; the
        // point is to fail loudly if a filter layer regresses and the ghosts return.
        Assert.True(open.Count < 50, $"expected a short list, got {open.Count}");
    }

    [Fact]
    public void Open_windows_all_have_a_title()
    {
        Assert.All(_service.GetOpenWindows(), w => Assert.NotEmpty(w.Title));
    }

    [Fact]
    public void Handles_are_unique_but_titles_need_not_be()
    {
        // Two Explorer windows on the same folder share a title and are both real, so
        // de-duplication happens by handle and never by text.
        var windows = _service.GetOpenWindows();

        Assert.Equal(windows.Count, windows.Select(w => w.Hwnd).Distinct().Count());
    }

    [Fact]
    public void Every_window_resolves_a_process()
    {
        Assert.All(_service.GetOpenWindows(), w =>
        {
            Assert.NotEmpty(w.ProcessName);
            Assert.NotEqual(0u, w.ProcessId);
        });
    }

    [Fact]
    public void At_most_one_window_is_foreground()
    {
        Assert.True(_service.GetOpenWindows().Count(w => w.IsForeground) <= 1);
    }
}
