using HeyAI.Core.Tools;
using HeyAI.Modules.Media.Audio;
using HeyAI.Modules.Media.Gsmtc;
using HeyAI.Modules.Media.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class HeyAIMediaServiceCollectionExtensions
{
    /// <summary>
    /// Media control over GSMTC and per-application volume over Core Audio.
    ///
    /// Both services are MTA-safe, so neither takes IWinRtDispatcher. See that
    /// interface's remarks before adding one here.
    /// </summary>
    public static IServiceCollection AddHeyAIMedia(this IServiceCollection services)
    {
        services.TryAddSingleton<MediaSessionService>();
        services.TryAddSingleton<AudioService>();

        // TryAddEnumerable, not AddSingleton: it de-duplicates by implementation type, so
        // calling AddHeyAIMedia twice cannot register a tool name twice and trip the
        // duplicate check in ToolRegistry.
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IHeyAITool, MediaGetStatusTool>(),
            ServiceDescriptor.Singleton<IHeyAITool, MediaControlTool>(),
            ServiceDescriptor.Singleton<IHeyAITool, AudioGetDevicesTool>(),
            ServiceDescriptor.Singleton<IHeyAITool, AudioSetVolumeTool>(),
        ]);

        return services;
    }
}
