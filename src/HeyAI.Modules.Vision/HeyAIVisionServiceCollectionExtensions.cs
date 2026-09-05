using HeyAI.Core.Tools;
using HeyAI.Modules.Vision;
using HeyAI.Modules.Vision.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class HeyAIVisionServiceCollectionExtensions
{
    /// <summary>
    /// Native screen capture and OCR.
    ///
    /// Direct3DDevice is a singleton because creating one costs tens of milliseconds and
    /// holds GPU resources; the container also owns disposing it. It is free-threaded,
    /// which is what makes sharing it safe now that tool calls run concurrently.
    ///
    /// Nothing here takes IWinRtDispatcher. Direct3D11CaptureFramePool.CreateFreeThreaded
    /// removes the DispatcherQueue requirement that the picker-based API has.
    /// </summary>
    public static IServiceCollection AddHeyAIVision(this IServiceCollection services)
    {
        services.TryAddSingleton<Direct3DDevice>();
        services.TryAddSingleton<CaptureService>();
        services.TryAddSingleton<OcrService>();

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IHeyAITool, OcrReadTextTool>(),
            ServiceDescriptor.Singleton<IHeyAITool, ScreenCaptureTool>(),
        ]);

        return services;
    }
}
