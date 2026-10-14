using System.Net;
using Kairo.SharedKernel.Http;

namespace Kairo.IntegrationTests.Web;

public sealed class BlockedAddressesTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::")]
    public void Local_private_and_metadata_addresses_are_blocked(string address) =>
        Assert.True(BlockedAddresses.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2607:f8b0:4004:800::200e")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.False(BlockedAddresses.IsBlocked(IPAddress.Parse(address)));
}
