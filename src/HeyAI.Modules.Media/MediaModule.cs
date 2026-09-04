using HeyAI.Core.Tools;
using HeyAI.Modules.Media.Audio;
using HeyAI.Modules.Media.Gsmtc;
using HeyAI.Modules.Media.Tools;

namespace HeyAI.Modules.Media;

/// <summary>
/// Registration surface for the module. A module owns its services and hands back tools;
/// the host does not know what a GSMTC session or an MMDevice is.
/// </summary>
public static class MediaModule
{
    public static IEnumerable<IHeyAITool> CreateTools()
    {
        var sessions = new MediaSessionService();
        var audio = new AudioService();

        yield return new MediaGetStatusTool(sessions);
        yield return new MediaControlTool(sessions);
        yield return new AudioGetDevicesTool(audio);
        yield return new AudioSetVolumeTool(audio);
    }
}
