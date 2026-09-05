namespace HeyAI.Core.Tools;

/// <summary>
/// Flat name to tool map. Modules register into this at host startup; registration is
/// separate from enablement, so a registered tool that the config does not allow is
/// still refused by the policy engine.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IHeyAITool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry()
    {
    }

    /// <summary>
    /// DI entry point: every module registers its tools as <see cref="IHeyAITool"/>, and
    /// the container hands the whole set here. Modules no longer expose a CreateTools().
    /// </summary>
    public ToolRegistry(IEnumerable<IHeyAITool> tools) => RegisterAll(tools);

    public void Register(IHeyAITool tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
        {
            throw new InvalidOperationException($"Duplicate tool name '{tool.Name}'.");
        }
    }

    public void RegisterAll(IEnumerable<IHeyAITool> tools)
    {
        foreach (var tool in tools) Register(tool);
    }

    public bool TryGet(string name, out IHeyAITool tool) => _tools.TryGetValue(name, out tool!);

    public IReadOnlyCollection<IHeyAITool> All =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
}
