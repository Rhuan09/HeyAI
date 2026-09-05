using HeyAI.Core.Tools;
using HeyAI.Modules.Shell.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class HeyAIShellServiceCollectionExtensions
{
    /// <summary>
    /// Opening files and folders.
    ///
    /// The module docs/NON-GOALS.md draws its line around: this opens things the shell
    /// already knows how to open, and will never grow a tool that runs a command string.
    /// Risk cannot be evaluated from an argument that is itself a program, and one such
    /// tool would make every other tier here decorative.
    /// </summary>
    public static IServiceCollection AddHeyAIShell(this IServiceCollection services)
    {
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IHeyAITool, ShellOpenPathTool>(),
        ]);

        return services;
    }
}
