using System.Diagnostics;
using System.Text.Json;
using HeyAI.Core.Audit;
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
    TaintTracker taint)
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

        if (decision.Outcome != PolicyOutcome.Allow)
        {
            audit.Write(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Tool = toolName,
                Risk = risk,
                Outcome = decision.Outcome,
                Reason = decision.Reason,
                ArgsHash = argsHash,
                Args = JsonlAuditLog.TruncateArgs(rawArgs),
                Client = client,
            });

            // RequireConfirmation still cannot reach a human, so it is reported as a
            // refusal carrying the remedy. The tray exists now, but a server is spawned
            // per client session while the tray is a separate standing process, so asking
            // it needs IPC. See docs/ARCHITECTURE.md, "Confirmation transport".
            var code = decision.Outcome == PolicyOutcome.Deny ? "denied_by_policy" : "confirmation_required";
            return ToolResult.Error(code, decision.Reason);
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
            Client = client,
        });

        return result;
    }
}
