using System.Text;
using System.Text.Json;
using HeyAI.Core;
using HeyAI.Core.Security;
using HeyAI.Core.Threading;
using HeyAI.Core.Tools;
using HeyAI.Modules.Window;
using Microsoft.Extensions.DependencyInjection;

namespace HeyAI.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // stderr is the only legal place to talk. stdout belongs to the protocol.
        var log = Console.Error;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        HeyAIPaths.EnsureCreated();

        // The composition root. Each module owns its own registrations, so adding one is
        // a single line here rather than an edit inside this method's body.
        var services = new ServiceCollection()
            .AddHeyAICore()
            .AddHeyAIMedia()
            .AddHeyAIWindow();

        // await using: the provider owns disposal of the audit log and, if anything woke
        // it, the dispatcher's STA thread.
        await using var provider = services.BuildServiceProvider();

        return args switch
        {
            [] or ["serve"] => await ServeAsync(provider, log, cts.Token).ConfigureAwait(false),
            ["list"] => ListTools(provider),
            ["test", .. var rest] => await TestToolAsync(provider, rest, log, cts.Token).ConfigureAwait(false),
            ["doctor"] => await DoctorAsync(provider, log).ConfigureAwait(false),
            ["windows"] => ListWindows(provider),
            _ => Usage(),
        };
    }

    private static async Task<int> ServeAsync(
        IServiceProvider provider, TextWriter log, CancellationToken ct)
    {
        // No BOM, no autoflush surprises: the client parses raw UTF-8 lines.
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = false,
        };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        var server = new Mcp.McpServer(
            provider.GetRequiredService<ToolRegistry>(),
            provider.GetRequiredService<ToolInvoker>(),
            log);

        await server.RunAsync(stdin, stdout, ct).ConfigureAwait(false);
        return 0;
    }

    private static int ListTools(IServiceProvider provider)
    {
        var registry = provider.GetRequiredService<ToolRegistry>();
        var config = provider.GetRequiredService<HeyAIConfig>();

        foreach (var tool in registry.All)
        {
            var state = config.IsEnabled(tool.Name) ? "enabled " : "disabled";
            var kind = tool.Annotations.ReadOnlyHint ? "read " : "write";
            Console.WriteLine($"{state}  {kind}  {tool.Name,-22} {tool.Title}");
        }

        Console.WriteLine();
        Console.WriteLine($"config: {HeyAIPaths.ConfigFile}");
        Console.WriteLine($"audit:  {HeyAIPaths.AuditLogFile}");
        return 0;
    }

    /// <summary>
    /// Exercises a tool through the full pipeline without an LLM in the loop. This is the
    /// loop you want while writing a module: heyai test media_get_status
    /// </summary>
    private static async Task<int> TestToolAsync(
        IServiceProvider provider, string[] rest, TextWriter log, CancellationToken ct)
    {
        if (rest.Length == 0)
        {
            log.WriteLine("usage: heyai test <tool_name> [json_args]");
            return 2;
        }

        var toolName = rest[0];
        var argsJson = rest.Length > 1 ? rest[1] : "{}";

        JsonElement args;
        try
        {
            args = JsonDocument.Parse(argsJson).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            log.WriteLine($"invalid json args: {ex.Message}");
            return 2;
        }

        var invoker = provider.GetRequiredService<ToolInvoker>();
        var result = await invoker.InvokeAsync(toolName, args, "heyai-cli", ct).ConfigureAwait(false);

        Console.WriteLine(result.Text);
        if (result.Tainted) log.WriteLine($"[heyai] output marked untrusted ({result.TaintSource})");
        return result.IsError ? 1 : 0;
    }

    /// <summary>
    /// TEMPORARY scaffolding. Prints raw window enumeration so the shape of the data can
    /// be seen before window_list_open's schema, risk tier and taint marking are designed.
    /// Delete this verb once that tool exists — it deliberately bypasses ToolInvoker,
    /// which is only acceptable because no model can ever reach it.
    /// </summary>
    private static int ListWindows(IServiceProvider provider)
    {
        var windows = provider.GetRequiredService<WindowService>().GetOpenWindows();

        foreach (var w in windows)
        {
            var flags = (w.IsForeground ? "*" : " ") + (w.IsMinimized ? "_" : " ");
            Console.WriteLine($"{flags} 0x{w.Hwnd:X8}  {w.ProcessName,-16}  {w.Title}");
        }

        Console.WriteLine();
        Console.WriteLine($"total: {windows.Count}");
        return 0;
    }

    /// <summary>
    /// Verifies the environment pieces that fail confusingly at runtime: the STA
    /// DispatcherQueue thread, and write access to the state directory.
    /// </summary>
    private static async Task<int> DoctorAsync(IServiceProvider provider, TextWriter log)
    {
        var ok = true;

        log.WriteLine($"state dir : {HeyAIPaths.Root}");
        log.WriteLine(PackageIdentity.IsPackaged
            ? $"identity  : packaged ({PackageIdentity.FullName})"
            : "identity  : unpackaged (toasts and the tray need MSIX identity)");
        log.WriteLine($"config    : {(File.Exists(HeyAIPaths.ConfigFile) ? "present" : "missing")}");

        try
        {
            // Resolving this is what starts the STA thread; the container disposes it.
            var dispatcher = provider.GetRequiredService<IWinRtDispatcher>();
            var apartment = await dispatcher.InvokeAsync(() => Thread.CurrentThread.GetApartmentState());
            log.WriteLine($"dispatcher: ready, apartment={apartment}");
            if (apartment != ApartmentState.STA)
            {
                log.WriteLine("dispatcher: FAIL expected STA");
                ok = false;
            }
        }
        catch (Exception ex)
        {
            log.WriteLine($"dispatcher: FAIL {ex.Message}");
            ok = false;
        }

        log.WriteLine(ok ? "doctor: ok" : "doctor: problems found");
        return ok ? 0 : 1;
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            """
            heyai - native Windows APIs for AI agents, over MCP.

              heyai [serve]              run the MCP server on stdio (default)
              heyai list                 list registered tools and whether they are enabled
              heyai test <tool> [json]   invoke one tool through the full policy pipeline
              heyai doctor               check the STA dispatcher and state directory
              heyai windows              TEMPORARY: raw window enumeration
            """);
        return 2;
    }
}
