using HeyAI.Core;

namespace HeyAI.Tray;

internal static class Program
{
    /// <summary>
    /// Local\ rather than Global\: one tray per signed-in user is right, and a Global
    /// mutex would let one user's session block another's on a shared machine.
    /// </summary>
    private const string InstanceMutexName = @"Local\HeyAI.Tray.SingleInstance";

    [STAThread]
    private static int Main()
    {
        // STA is not optional. Shell notification icons, the context menu and any dialog
        // this grows later are all apartment-affine, and WinForms asserts on MTA.
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isOnlyInstance);

        if (!isOnlyInstance)
        {
            // Two trays would mean two icons and two views of the same audit log, which
            // reads as a bug. Exiting quietly is what a second launch should do.
            return 0;
        }

        ApplicationConfiguration.Initialize();
        HeyAIPaths.EnsureCreated();

        try
        {
            using var context = new TrayContext();
            Application.Run(context);
            return 0;
        }
        catch (Exception ex)
        {
            // There is no console to print to and no client to return an error to, so the
            // only honest failure mode is to say so once and leave.
            MessageBox.Show(
                $"HeyAI tray could not start.\n\n{ex.Message}",
                "HeyAI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
