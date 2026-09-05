using HeyAI.Core;
using HeyAI.Core.Audit;
using HeyAI.Core.Confirmation;
using HeyAI.Core.Security;
using HeyAI.Core.Threading;
using HeyAI.Core.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Namespace is deliberately Microsoft.Extensions.DependencyInjection rather than
// HeyAI.Core.*, following the convention every Add* extension in .NET uses: the caller
// already has this using for ServiceCollection, so AddHeyAICore() shows up without an
// extra import.
namespace Microsoft.Extensions.DependencyInjection;

public static class HeyAICoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the invocation pipeline. Call once, before any module's Add*.
    ///
    /// Everything is a singleton: heyai is one process per client session with no
    /// concurrent users, so scoped and transient lifetimes have nothing to express here.
    ///
    /// Disposal is the container's job now. <see cref="JsonlAuditLog"/> is IDisposable and
    /// <see cref="StaWinRtDispatcher"/> is IAsyncDisposable holding a native thread, and
    /// the provider disposes them in reverse registration order on shutdown.
    /// </summary>
    public static IServiceCollection AddHeyAICore(
        this IServiceCollection services, HeyAIConfig? config = null)
    {
        services.TryAddSingleton(config ?? HeyAIConfig.Load());

        // Factory lambdas rather than type registration: both have optional constructor
        // parameters (a TimeProvider, a path) that exist for tests, and letting the
        // container try to satisfy them invites a confusing resolution failure.
        services.TryAddSingleton(_ => new TaintTracker());
        services.TryAddSingleton<IAuditLog>(_ => new JsonlAuditLog());

        services.TryAddSingleton<IPolicyEngine, PolicyEngine>();

        // Fail closed by default. A host that forgets to wire a real prompt refuses
        // Critical actions rather than quietly allowing them; the server replaces this
        // with the named-pipe prompt that reaches the tray.
        services.TryAddSingleton<IConfirmationPrompt, DenyingConfirmationPrompt>();

        // Resolved lazily, so the STA thread and its message pump only start if something
        // actually needs them. Nothing in the Media module does; Vision will.
        services.TryAddSingleton<IWinRtDispatcher, StaWinRtDispatcher>();

        // Populated from every IHeyAITool the modules registered.
        services.TryAddSingleton<ToolRegistry>();

        // The single door. Nothing outside Program should resolve IHeyAITool directly and
        // call ExecuteAsync — that bypasses risk evaluation, policy, audit and taint.
        services.TryAddSingleton<ToolInvoker>();

        return services;
    }
}
