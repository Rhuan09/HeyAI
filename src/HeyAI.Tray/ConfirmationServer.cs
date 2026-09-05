using System.Text;
using System.Text.Json;
using HeyAI.Core.Confirmation;

namespace HeyAI.Tray;

/// <summary>
/// Listens for confirmation requests from HeyAI servers and answers them by asking a
/// person.
///
/// Several MCP clients can be connected at once, each with its own server process, so more
/// than one request can be in flight. They are answered one at a time because a human is:
/// two security dialogs racing for the same click is how the wrong one gets approved.
/// </summary>
internal sealed class ConfirmationServer : IAsyncDisposable
{
    private const int MaxInstances = 4;

    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly Func<ConfirmationRequest, Task<bool>> _ask;
    private readonly Action<string> _log;
    private readonly Task _loop;

    public ConfirmationServer(Func<ConfirmationRequest, Task<bool>> ask, Action<string> log)
    {
        _ask = ask;
        _log = log;
        _loop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        var name = ConfirmationPipe.CurrentUserName();

        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                var pipe = ConfirmationPipe.CreateServer(name, MaxInstances);
                await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);

                // Handled off the accept loop so the next request can connect while this
                // one waits on a person.
                _ = HandleAsync(pipe);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log($"confirmation listener: {ex.Message}");

                // Do not spin on a persistent failure -- another process holding the pipe
                // name, for instance -- or this burns a core until the tray is killed.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleAsync(System.IO.Pipes.NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true,
                };

                var line = await reader.ReadLineAsync(_stopping.Token).ConfigureAwait(false);
                if (line is null) return;

                var request = JsonSerializer.Deserialize<ConfirmationRequest>(
                    line, ConfirmationPipe.Json);

                if (request is null)
                {
                    await Reply(writer, ConfirmationResponse.Denied("unreadable request"))
                        .ConfigureAwait(false);
                    return;
                }

                await _oneAtATime.WaitAsync(_stopping.Token).ConfigureAwait(false);
                bool approved;
                try
                {
                    approved = await _ask(request).ConfigureAwait(false);
                }
                finally
                {
                    _oneAtATime.Release();
                }

                await Reply(writer, approved
                    ? ConfirmationResponse.Approved_("a person approved it at the tray prompt")
                    : ConfirmationResponse.Denied("a person refused it at the tray prompt"))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The asking side treats a dropped pipe as a denial, so failing here is
                // safe. Losing the tray to one malformed request would not be.
                _log($"confirmation request: {ex.Message}");
            }
        }
    }

    private static Task Reply(StreamWriter writer, ConfirmationResponse response) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(response, ConfirmationPipe.Json));

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Shutting down; a listener stuck on a connection is not worth waiting for.
        }

        _stopping.Dispose();
        _oneAtATime.Dispose();
    }
}
