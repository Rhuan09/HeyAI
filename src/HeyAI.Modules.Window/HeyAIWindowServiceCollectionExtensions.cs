using HeyAI.Core.Tools;
using HeyAI.Modules.Window;
using HeyAI.Modules.Window.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class HeyAIWindowServiceCollectionExtensions
{
    /// <summary>
    /// Win32 window enumeration and management.
    ///
    /// WindowService is MTA-safe plain user32, so it does not take IWinRtDispatcher.
    /// </summary>
    public static IServiceCollection AddHeyAIWindow(this IServiceCollection services)
    {
        services.TryAddSingleton<WindowService>();

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IHeyAITool, WindowListOpenTool>(),
        ]);

        return services;
    }
}
