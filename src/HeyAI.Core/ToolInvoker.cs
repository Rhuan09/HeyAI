using System.Diagnostics;
using System.Text.Json;
using HeyAI.Core.Audit;
using HeyAI.Core.Confirmation;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;

namespace HeyAI.Core;

/// <summary>
/// The single path every tool call takes. Nothing may invoke <see cref="IHeyAITool"/>
/// directly -- the risk evaluation, policy check, audit record and taint update are all
/// here, and bypassing this bypasses the security model wholesale.
///
///   evaluate risk -> policy -> audit(decision) -> execute -> taint -> audit(outcome)
/// </summary>
public sealed class ToolInvoker(
    ToolRegistry registry,
    IPolicyEngine policy,
    IAuditLog audit,
    TaintTracker taint,
    IConfirmationPrompt prompt)
{
    public async Task<ToolResult> InvokeAsync(
        string toolName, JsonElement args, string? client, CancellationToken ct)
    {
        if (!registry.TryGet(toolName, out var tool))
        {
            return ToolResult.Error("unknown_tool", $"No tool named '{toolName}'.");
        }

        var rawArgs = args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText();
        var argsHash = JsonlAuditLog.HashArgs(rawArgs);

        RiskLevel risk;
        try
        {
            risk = tool.EvaluateRisk(args);
        }
        catch (Exception ex)
        {
            // A tool that cannot classify its own arguments is treated as maximally risky.
            risk = RiskLevel.Critical;
            audit.Write(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Tool = toolName,
                Risk = risk,
                Outcome = PolicyOutcome.Deny,
                Reason = $"risk evaluation threw: {ex.GetType().Name}",
                ArgsHash = argsHash,
                Args = JsonlAuditLog.TruncateArgs(rawArgs),
                Client = client,
            });
            return ToolResult.Error("risk_evaluation_failed",
                "Arguments could not be classified for risk, so the call was refused.");
        }

        var decision = policy.Evaluate(tool, args, risk);

        if (decision.Outcome == PolicyOutcome.Deny)
        {
            audit.Write(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Tool = toolName,
                Risk = risk,
                Outcome = PolicyOutcome.Deny,
                Reason = decision.Reason,
                ArgsHash = argsHash,
                Args = JsonlAuditLog.TruncateArgs(rawArgs),
                Client = client,
            });

            return ToolResult.Error("denied_by_policy", decision.Reason);
        }

        bool? confirmedByHuman = null;

        if (decision.Outcome == PolicyOutcome.RequireConfirmation)
        {
            // Arguments are truncated before they reach a dialog. They can be long, and
            // they can contain text an attacker chose -- a window title, a path lifted
            // from OCR -- so the prompt gets a bounded string to render as inert data.
            var answer = await prompt.AskAsync(new ConfirmationRequest
            {
                ToolName = tool.Name,
                ToolTitle = tool.Title,
                Risk = risk,
                Reason = decision.Reason,
                ArgumentsJson = JsonlAuditLog.TruncateArgs(rawArgs) ?? "{}",
                Client = client,
            }, ct).ConfigureAwait(false);

            confirmedByHuman = answer.Approved;

            audit.Write(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Tool = toolName,
                Risk = risk,
                Outcome = PolicyOutcome.RequireConfirmation,
                Reason = answer.Detail,
                ArgsHash = argsHash,
                Args = JsonlAuditLog.TruncateArgs(rawArgs),
                ConfirmedByHuman = answer.Approved,
                Client = client,
            });

            if (!answer.Approved)
            {
                return ToolResult.Error("confirmation_denied", answer.Detail);
            }
        }

        var sw = Stopwatch.StartNew();
        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(args, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result = ToolResult.Error("cancelled", "The call was cancelled.");
        }
        catch (Exception ex)
        {
            // A tool throwing is a bug in that tool, never a reason to kill the server:
            // the process is a live stdio transport for the client.
            result = ToolResult.Error("tool_faulted", $"{tool.Name} faulted: {ex.Message}");
        }

        sw.Stop();

        if (result.Tainted && result.TaintSource is not null)
        {
            taint.RecordUntrustedRead(result.TaintSource);
        }

        audit.Write(new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Tool = toolName,
            Risk = risk,
            Outcome = PolicyOutcome.Allow,
            Reason = decision.Reason,
            ArgsHash = argsHash,
            Args = JsonlAuditLog.TruncateArgs(rawArgs),
            DurationMs = sw.ElapsedMilliseconds,
            Failed = result.IsError,
            ErrorCode = result.ErrorCode,
            ProducedTaintedOutput = result.Tainted,
            ConfirmedByHuman = confirmedByHuman,
            Client = client,
        });

        return result;
    }
}
