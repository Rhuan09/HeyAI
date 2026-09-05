namespace HeyAI.Core;

/// <summary>
/// All HeyAI state lives under %LOCALAPPDATA%\HeyAI. Never ~/.heyai — that is a POSIX
/// convention and lands in the roaming-adjacent profile root on Windows.
/// </summary>
public static class HeyAIPaths
{
    /// <summary>
    /// True when the OS is silently redirecting this process's writes under these paths.
    ///
    /// MSIX applies file system redirection to AppData, transparently: the app computes
    /// and reports the normal path, File.Exists agrees, and the bytes actually land in a
    /// per-package container. A packaged and an unpackaged install therefore have
    /// different config and different audit logs while both claiming the same location.
    ///
    /// Verified by enabling a tool in one and watching the other still report it disabled.
    /// Nothing here can undo the redirection; the point is to stop reporting a path that
    /// is not where the bytes are. See docs/ARCHITECTURE.md, "State and packaging".
    /// </summary>
    public static bool IsRedirected => PackageIdentity.IsPackaged;

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyAI");

    public static string ConfigFile => Path.Combine(Root, "config.json");

    public static string LogDirectory => Path.Combine(Root, "logs");

    public static string AuditLogFile => Path.Combine(LogDirectory, "audit.jsonl");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>
    /// Paths the agent must never be able to reach through a filesystem or shell tool.
    /// An audit log the agent can open is an audit log the agent can tamper with.
    /// </summary>
    public static bool IsProtected(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(Root, StringComparison.OrdinalIgnoreCase);
    }
}
