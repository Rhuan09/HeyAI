using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace HeyAI.Core.Confirmation;

/// <summary>
/// The wire between a server and the tray.
///
/// A server is spawned per client session and has no UI; the tray is one standing process
/// that does. A named pipe is the smallest thing that connects them: no port to collide,
/// no listener reachable from off the machine, and the OS handles the lifetime.
///
/// Protocol is one JSON line in, one JSON line out, then close. Deliberately not a
/// long-lived session — a confirmation is a single question, and a stateless exchange
/// cannot get out of step.
/// </summary>
public static class ConfirmationPipe
{
    /// <summary>
    /// Per-user by construction. Two people signed into the same machine must not be able
    /// to see, answer, or spoof each other's prompts, and the pipe namespace is global.
    /// </summary>
    public static string NameFor(string userSid) => $"HeyAI.Confirm.v1.{userSid}";

    public static string CurrentUserName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return NameFor(identity.User?.Value ?? identity.Name.Replace('\\', '_'));
    }

    /// <summary>
    /// Both ends must serialise identically or the exchange fails in a way that looks like
    /// a hostile answer, so this is part of the wire contract rather than an implementation
    /// detail of either side.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// Creates the listening end with an ACL that admits only the current user.
    ///
    /// Without this the default DACL would let any authenticated user connect, and
    /// answering someone else's security prompt is exactly the thing this must not allow.
    /// </summary>
    public static NamedPipeServerStream CreateServer(string name, int maxInstances)
    {
        using var identity = WindowsIdentity.GetCurrent();

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            identity.User!,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }
}

/// <summary>
/// The asking end, used by the server.
///
/// Every failure path answers "denied". If a broken pipe or a stopped tray meant "allowed",
/// then killing the tray would be the easiest privilege escalation on the machine.
/// </summary>
public sealed class NamedPipeConfirmationPrompt(TimeSpan? timeout = null, string? pipeName = null)
    : IConfirmationPrompt
{
    /// <summary>
    /// Long enough for someone to read a dialog and decide, short enough that an
    /// unattended machine releases the call instead of holding it forever. The transport
    /// handles requests concurrently, so a pending prompt does not block other calls.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;
    private readonly string _pipeName = pipeName ?? ConfirmationPipe.CurrentUserName();

    public async Task<ConfirmationResponse> AskAsync(
        ConfirmationRequest request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeout);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            // A short connect timeout separates "the tray is not running" from "nobody is
            // at the keyboard". The first should say so immediately rather than making the
            // caller wait out the full window for an answer that will never come.
            await pipe.ConnectAsync(TimeSpan.FromSeconds(2), deadline.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);

            await writer.WriteLineAsync(
                JsonSerializer.Serialize(request, ConfirmationPipe.Json).AsMemory(), deadline.Token)
                .ConfigureAwait(false);

            var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (line is null)
            {
                return ConfirmationResponse.Denied("the prompt closed without answering");
            }

            return JsonSerializer.Deserialize<ConfirmationResponse>(line, ConfirmationPipe.Json)
                   ?? ConfirmationResponse.Denied("the prompt returned nothing readable");
        }
        catch (TimeoutException)
        {
            return ConfirmationResponse.Denied(
                "the HeyAI tray is not running, so there is nobody to ask. Start it, or " +
                "lower maxAutoApprovedRisk only if you understand what that allows.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ConfirmationResponse.Denied(
                $"nobody answered within {_timeout.TotalSeconds:F0} seconds");
        }
        catch (Exception ex)
        {
            return ConfirmationResponse.Denied($"could not reach the tray: {ex.Message}");
        }
    }
}
