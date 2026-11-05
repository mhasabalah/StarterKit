using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Webhooks.IntegrationTests;

public sealed record ReceivedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body);

public sealed record ReceiverResponse(int Status = 200, string Body = "ok", TimeSpan? Delay = null,
    IReadOnlyDictionary<string, string>? Headers = null, TimeSpan? StallAfterPartialBody = null);

/// <summary>
/// A tiny HTTP/1.1 server on loopback (no framework): records what the sender really put on the wire and answers with a
/// scripted response. Lets the tests prove headers, signature, redirects, timeouts and the response cap end to end.
/// </summary>
public sealed class TestReceiver : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<ReceivedRequest> _requests = new();
    private readonly Task _loop;

    public TestReceiver()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/hook";

    public IReadOnlyList<ReceivedRequest> Requests => _requests.ToList();

    /// <summary>Decides the answer per request. Default: 200 ok.</summary>
    public Func<ReceivedRequest, ReceiverResponse> Respond { get; set; } = _ => new ReceiverResponse();

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var request = await ReadRequest(stream);
            if (request is null) return;
            _requests.Enqueue(request);

            var response = Respond(request);
            if (response.Delay is { } delay) await Task.Delay(delay, _cts.Token);

            var body = Encoding.UTF8.GetBytes(response.Body);
            var head = new StringBuilder()
                .Append($"HTTP/1.1 {response.Status} X\r\n")
                .Append($"Content-Length: {body.Length}\r\n")
                .Append("Content-Type: text/plain\r\n")
                .Append("Connection: close\r\n");
            foreach (var (name, value) in response.Headers ?? new Dictionary<string, string>())
                head.Append($"{name}: {value}\r\n");
            head.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _cts.Token);
            if (response.StallAfterPartialBody is { } stall)
            {
                // Status line + headers + the first half of the declared body, then silence.
                await stream.WriteAsync(body.AsMemory(0, body.Length / 2), _cts.Token);
                await stream.FlushAsync(_cts.Token);
                await Task.Delay(stall, _cts.Token);
                return;
            }

            await stream.WriteAsync(body, _cts.Token);
            await stream.FlushAsync(_cts.Token);
        }
        catch (Exception)
        {
            // The client gave up (timeout) or the test ended: nothing to do.
        }
    }

    private static async Task<ReceivedRequest?> ReadRequest(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) == 0) return null;
            buffer.Add(one[0]);
            var n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n') break;
        }

        var lines = Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var first = lines[0].Split(' ');
        var headers = lines.Skip(1).Select(l => l.Split(':', 2))
            .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

        var length = headers.TryGetValue("content-length", out var value) ? int.Parse(value) : 0;
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(body.AsMemory(read, length - read));
            if (count == 0) break;
            read += count;
        }

        return new ReceivedRequest(first[0], first[1], headers, Encoding.UTF8.GetString(body, 0, read));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
            // shutting down
        }

        _cts.Dispose();
    }
}
