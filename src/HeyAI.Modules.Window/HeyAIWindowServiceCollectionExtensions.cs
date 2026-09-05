using HeyAI.Modules.Window;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class HeyAIWindowServiceCollectionExtensions
{
    /// <summary>
    /// Win32 window enumeration and management.
    ///
    /// No tools registered yet — the service is being proven against a real desktop
    /// before its schema and risk classification are designed.
    /// </summary>
    public static IServiceCollection AddHeyAIWindow(this IServiceCollection services)
    {
        services.TryAddSingleton<WindowService>();
        return services;
    }
}
