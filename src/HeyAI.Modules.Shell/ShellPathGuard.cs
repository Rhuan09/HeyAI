using HeyAI.Core;

namespace HeyAI.Modules.Shell;

public sealed record PathVerdict(bool Allowed, string? Reason, string? FullPath)
{
    public static PathVerdict Refuse(string reason) => new(false, reason, null);
    public static PathVerdict Allow(string fullPath) => new(true, null, fullPath);
}

/// <summary>
/// Decides whether a path may be handed to the shell at all.
///
/// Separate from risk classification on purpose. Risk answers "how much does this need
/// approval"; this answers "is this something HeyAI will ever do", and the answers here
/// are not negotiable by raising a ceiling in config.
///
/// Runs at execution time, not in EvaluateRisk, because it touches the filesystem and
/// EvaluateRisk must stay pure -- see CONTRIBUTING.
/// </summary>
public static class ShellPathGuard
{
    public static PathVerdict Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathVerdict.Refuse("No path was given.");
        }

        // This tool takes filesystem paths. A URI would let it reach the network or an
        // arbitrary protocol handler, which is a different tool with a different threat
        // model, not a convenience to fold in here.
        if (path.Contains("://", StringComparison.Ordinal))
        {
            return PathVerdict.Refuse(
                "That looks like a URL. shell_open_path opens files and folders only.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return PathVerdict.Refuse($"Not a usable path: {ex.Message}");
        }

        // Resolved before the protected check, so a path walking out through .. or a
        // junction cannot be used to reach the state directory sideways.
        if (HeyAIPaths.IsProtected(full))
        {
            return PathVerdict.Refuse(
                "That path is inside HeyAI's own state directory. The audit log and the " +
                "permission config are not reachable through a tool.");
        }

        if (!File.Exists(full) && !Directory.Exists(full))
        {
            // Said plainly rather than launched hopefully: ShellExecute on a missing path
            // can still pop a "how do you want to open this" dialog at the user.
            return PathVerdict.Refuse($"Nothing exists at {full}.");
        }

        return PathVerdict.Allow(full);
    }
}
