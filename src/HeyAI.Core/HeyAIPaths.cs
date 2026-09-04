namespace HeyAI.Core;

/// <summary>
/// All HeyAI state lives under %LOCALAPPDATA%\HeyAI. Never ~/.heyai — that is a POSIX
/// convention and lands in the roaming-adjacent profile root on Windows.
/// </summary>
public static class HeyAIPaths
{
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
