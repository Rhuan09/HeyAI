using HeyAI.Core.Tools;

namespace HeyAI.Core.Confirmation;

/// <summary>
/// What a human is being asked to approve.
///
/// Everything here is shown in a security dialog, and <see cref="ArgumentsJson"/> can
/// contain text an attacker chose — a window title, a path from OCR. The prompt is
/// responsible for rendering it as inert data; see the tray's dialog.
/// </summary>
public sealed record ConfirmationRequest
{
    public required string ToolName { get; init; }
    public required string ToolTitle { get; init; }
    public required RiskLevel Risk { get; init; }

    /// <summary>Why policy stopped here, in the policy engine's words.</summary>
    public required string Reason { get; init; }

    public required string ArgumentsJson { get; init; }

    /// <summary>Which MCP client asked, as it identified itself. Not verified.</summary>
    public string? Client { get; init; }
}

public sealed record ConfirmationResponse(bool Approved, string Detail)
{
    public static ConfirmationResponse Denied(string detail) => new(false, detail);
    public static ConfirmationResponse Approved_(string detail) => new(true, detail);
}

/// <summary>
/// Asks a human whether an action may proceed.
///
/// Fails closed, always. Every implementation must answer "denied" when it cannot reach a
/// person — no tray running, a timeout, a broken pipe — because the alternative is that
/// killing the prompt becomes a way to approve things.
/// </summary>
public interface IConfirmationPrompt
{
    Task<ConfirmationResponse> AskAsync(ConfirmationRequest request, CancellationToken ct);
}

/// <summary>
/// Refuses everything. The default when nothing better is registered, so a host that
/// forgets to wire a prompt denies rather than silently allows.
/// </summary>
public sealed class DenyingConfirmationPrompt : IConfirmationPrompt
{
    public Task<ConfirmationResponse> AskAsync(ConfirmationRequest request, CancellationToken ct) =>
        Task.FromResult(ConfirmationResponse.Denied("no confirmation prompt is available"));
}
