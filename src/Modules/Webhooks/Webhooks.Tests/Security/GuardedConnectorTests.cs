using Shared.Core.Net;
using System.Net;
using Webhooks.Security;

namespace Webhooks.Tests.Security;

/// <summary>The connect-time SSRF guard: what a HOST NAME resolves to is what counts, not what the URL looks like.</summary>
public class GuardedConnectorTests
{
    private static GuardedConnector WithDns(bool allowPrivate, params string[] addresses) =>
        new(allowPrivate, (_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray()));

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.0.20")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.1.1")]
    [InlineData("::1")]
    [InlineData("fd00::5")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:169.254.169.254")]
    public async Task A_public_looking_name_that_resolves_to_a_private_address_is_rejected(string resolvedTo)
    {
        var connector = WithDns(allowPrivate: false, resolvedTo);

        var act = () => connector.ResolveCheckedAsync("totally-public.example.com", CancellationToken.None);

        await act.Should().ThrowAsync<BlockedNetworkAddressException>();
    }

    [Fact]
    public async Task One_private_record_among_public_ones_rejects_the_whole_host()
    {
        var connector = WithDns(false, "93.184.216.34", "10.0.0.5");

        var act = () => connector.ResolveCheckedAsync("mixed.example.com", CancellationToken.None);

        await act.Should().ThrowAsync<BlockedNetworkAddressException>();
    }

    [Fact]
    public async Task A_name_that_resolves_only_to_public_addresses_is_allowed()
    {
        var connector = WithDns(false, "93.184.216.34", "2606:4700:4700::1111");

        var addresses = await connector.ResolveCheckedAsync("ok.example.com", CancellationToken.None);

        addresses.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("10.1.2.3")]
    [InlineData("[::ffff:10.1.2.3]")]
    public async Task A_literal_private_ip_host_is_rejected_without_any_dns(string host)
    {
        var connector = new GuardedConnector(false, (_, _) => throw new InvalidOperationException("DNS must not be used"));

        var act = () => connector.ResolveCheckedAsync(host, CancellationToken.None);

        await act.Should().ThrowAsync<BlockedNetworkAddressException>();
    }

    [Fact]
    public async Task The_real_resolver_blocks_localhost()
    {
        var connector = new GuardedConnector(allowPrivateNetworks: false);

        var act = () => connector.ResolveCheckedAsync("localhost", CancellationToken.None);

        await act.Should().ThrowAsync<BlockedNetworkAddressException>("localhost resolves to 127.0.0.1 / ::1");
    }

    [Fact]
    public async Task The_development_toggle_lets_private_addresses_through()
    {
        var connector = WithDns(allowPrivate: true, "10.0.0.5");

        var addresses = await connector.ResolveCheckedAsync("intranet.example.com", CancellationToken.None);

        addresses.Single().ToString().Should().Be("10.0.0.5");
    }

    [Fact]
    public async Task A_name_that_does_not_resolve_is_a_connection_failure_not_a_block()
    {
        var connector = new GuardedConnector(false, (_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        var act = () => connector.ResolveCheckedAsync("nx.example.com", CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>()).And.Should().NotBeOfType<BlockedNetworkAddressException>();
    }
}
