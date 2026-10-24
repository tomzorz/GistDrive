using System.IO.Pipes;
using System.Text.Json;

namespace GistDrive;

/// <summary>
/// A request from an Explorer menu entry, a Send To entry or the command line, for the
/// background run of GistDrive.
/// </summary>
public sealed record VerbRequest(string Verb, string[] Paths);

/// <summary>
/// One JSON line per request over a named pipe that only the current user can open.
/// </summary>
public static class Ipc
{
    public static string PipeName => $"GistDrive-{Environment.UserName}";

    /// <summary>
    /// Serves requests until the token is cancelled. The handler only queues work, so each
    /// reply comes back at once.
    /// </summary>
    public static async Task ServeAsync(string pipeName, Func<VerbRequest, Task> handle, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(cancellationToken);
                var request = line is null ? null : JsonSerializer.Deserialize<VerbRequest>(line);
                if (request is not null) await handle(request);
                await writer.WriteLineAsync(request is null ? "bad request" : "ok");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Info($"pipe: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Sends a request. Returns false when no background run answered within the timeout.
    /// </summary>
    public static async Task<bool> SendAsync(string pipeName, VerbRequest request, TimeSpan timeout)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            return await reader.ReadLineAsync() == "ok";
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }
}
