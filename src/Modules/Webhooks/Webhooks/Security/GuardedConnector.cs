using System.Net;
using System.Net.Sockets;

namespace Webhooks.Security;

/// <summary>The connection target resolves to (or is) an address webhooks must never reach.</summary>
public sealed class WebhookBlockedAddressException(string host, string reason)
    : HttpRequestException($"Webhook target '{host}' is not allowed: {reason}");

/// <summary>
/// The SSRF guard that matters: a <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves DNS ITSELF, rejects
/// the host when ANY resolved address is non-public, then connects to the already-checked IP (never re-resolving).
/// Resolving once and connecting to that exact address closes the DNS-rebinding window between "check" and "use";
/// the TLS layer still validates the certificate against the original host name.
/// </summary>
public sealed class GuardedConnector(
    bool allowPrivateNetworks,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve =
        resolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await ResolveCheckedAsync(endpoint.Host, cancellationToken);

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException($"Could not connect to '{endpoint.Host}'.", last);
    }

    /// <summary>Resolves <paramref name="host"/> and throws unless EVERY address is acceptable.</summary>
    public async Task<IPAddress[]> ResolveCheckedAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _resolve(host, cancellationToken);
            }
            catch (SocketException ex)
            {
                throw new HttpRequestException($"Could not resolve '{host}'.", ex);
            }
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"Could not resolve '{host}'.");
        }

        if (!allowPrivateNetworks)
        {
            // Reject if ANY record is blocked: a mixed answer is a classic rebinding / split-horizon trick.
            var blocked = addresses.FirstOrDefault(NetworkAddressPolicy.IsBlocked);
            if (blocked is not null)
            {
                throw new WebhookBlockedAddressException(host, "resolves to a non-public address");
            }
        }

        return addresses;
    }
}
