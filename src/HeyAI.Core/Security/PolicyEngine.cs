using System.Text.Json;
using HeyAI.Core.Tools;

namespace HeyAI.Core.Security;

public enum PolicyOutcome
{
    Allow,
    Deny,

    /// <summary>
    /// Needs a human. Phase 1 has no tray, so <c>ToolInvoker</c> converts this into a
    /// denial carrying instructions. Phase 3 routes it to the tray for a real prompt.
    /// </summary>
    RequireConfirmation,
}

public sealed record PolicyDecision(PolicyOutcome Outcome, string Reason)
{
    public static PolicyDecision Allow() => new(PolicyOutcome.Allow, "allowed by policy");
}

public interface IPolicyEngine
{
    PolicyDecision Evaluate(IHeyAITool tool, JsonElement args, RiskLevel risk);
}

public sealed class PolicyEngine(HeyAIConfig config, TaintTracker taint) : IPolicyEngine
{
    public PolicyDecision Evaluate(IHeyAITool tool, JsonElement args, RiskLevel risk)
    {
        // 1. Allowlist. Deny-by-default is the whole point; check it before anything else.
        if (!config.IsEnabled(tool.Name))
        {
            return new PolicyDecision(
                PolicyOutcome.Deny,
                $"'{tool.Name}' is not enabled. Add it to enabledTools in {HeyAIPaths.ConfigFile}.");
        }

        // 2. Read-then-execute. A Critical action shortly after untrusted screen content
        //    is the injection chain, so it is refused outright rather than confirmed —
        //    a user who has just told the agent to read a page will approve the follow-up.
        if (risk == RiskLevel.Critical && config.BlockCriticalAfterUntrustedRead)
        {
            var window = TimeSpan.FromSeconds(config.UntrustedReadCooldownSeconds);
            if (taint.IsTainted(window, out var source, out var age))
            {
                return new PolicyDecision(
                    PolicyOutcome.Deny,
                    $"Critical action blocked: this session read untrusted content from " +
                    $"'{source}' {age.TotalSeconds:F0}s ago. Re-issue the request in a fresh session.");
            }
        }

        // 3. Risk ceiling.
        if (risk > config.MaxAutoApprovedRisk)
        {
            return new PolicyDecision(
                PolicyOutcome.RequireConfirmation,
                $"'{tool.Name}' evaluated to risk {risk}, above the configured ceiling " +
                $"{config.MaxAutoApprovedRisk}.");
        }

        return PolicyDecision.Allow();
    }
}
