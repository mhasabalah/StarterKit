using System.Net;
using Webhooks.Security;

namespace Webhooks.Tests.Security;

public class NetworkAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("169.254.0.1")]
    [InlineData("100.64.0.1")]      // carrier-grade NAT
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    public void Private_loopback_linklocal_and_reserved_IPv4_are_blocked(string ip) =>
        NetworkAddressPolicy.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue();

    [Theory]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("febf::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:0:4137:9e76::1")] // Teredo
    public void Loopback_unique_local_linklocal_and_reserved_IPv6_are_blocked(string ip) =>
        NetworkAddressPolicy.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue();

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:100.64.0.1")]
    [InlineData("::ffff:0.0.0.0")]
    [InlineData("::127.0.0.1")]          // IPv4-compatible (deprecated)
    [InlineData("64:ff9b::7f00:1")]      // NAT64 -> 127.0.0.1
    [InlineData("64:ff9b::a00:1")]       // NAT64 -> 10.0.0.1
    [InlineData("2002:7f00:1::1")]       // 6to4 -> 127.0.0.1
    [InlineData("2002:a9fe:a9fe::1")]    // 6to4 -> 169.254.169.254
    public void Private_IPv4_smuggled_inside_IPv6_is_blocked(string ip) =>
        NetworkAddressPolicy.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")] // just below 172.16/12
    [InlineData("172.32.0.1")]     // just above
    [InlineData("100.63.255.255")] // just below 100.64/10
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]  // NAT64 -> 8.8.8.8
    [InlineData("2002:808:808::1")]   // 6to4 -> 8.8.8.8
    public void Public_addresses_are_allowed(string ip) =>
        NetworkAddressPolicy.IsBlocked(IPAddress.Parse(ip)).Should().BeFalse();
}
