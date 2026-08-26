using System.Net;
using AnafAutoToken.Shared.Networking;
using FluentAssertions;

namespace AnafAutoToken.Tests.Networking;

public class NetworkAccessPolicyTests
{
    private static readonly string[] ConfiguredNetworks =
    [
        "192.168.21.0/24",
        "100.100.0.0/24",
        "192.168.29.0/24"
    ];

    [Theory]
    [InlineData("192.168.21.1")]
    [InlineData("192.168.21.255")]
    [InlineData("100.100.0.17")]
    [InlineData("192.168.29.200")]
    public void IsAllowed_AcceptsAddressesInsideTheConfiguredNetworks(string address)
    {
        var policy = NetworkAccessPolicy.Create(ConfiguredNetworks);

        policy.IsAllowed(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("192.168.22.1", "sąsiednia podsieć")]
    [InlineData("192.168.20.255", "podsieć tuż przed zakresem")]
    [InlineData("100.100.1.1", "trzeci oktet poza /24")]
    [InlineData("192.168.28.10", "podsieć tuż przed zakresem")]
    [InlineData("10.0.0.5", "zupełnie inna sieć")]
    [InlineData("172.21.64.1", "inny interfejs tej maszyny")]
    public void IsAllowed_RejectsAddressesOutsideTheConfiguredNetworks(string address, string why)
    {
        var policy = NetworkAccessPolicy.Create(ConfiguredNetworks);

        policy.IsAllowed(IPAddress.Parse(address)).Should().BeFalse(why);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void IsAllowed_AlwaysAcceptsLoopback(string address)
    {
        // Menedżer woła API z tej samej maszyny - odcięcie pętli zwrotnej zepsułoby
        // przycisk ręcznego odświeżania.
        var policy = NetworkAccessPolicy.Create(ConfiguredNetworks);

        policy.IsAllowed(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_HandlesIPv4AddressesMappedIntoIPv6()
    {
        // Gniazdo dual-stack podaje adresy IPv4 właśnie w tej formie.
        var policy = NetworkAccessPolicy.Create(ConfiguredNetworks);

        policy.IsAllowed(IPAddress.Parse("::ffff:192.168.21.50")).Should().BeTrue();
        policy.IsAllowed(IPAddress.Parse("::ffff:10.0.0.1")).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_WithoutConfiguredNetworks_AcceptsOnlyLoopback()
    {
        var policy = NetworkAccessPolicy.Create(null);

        policy.AllowsOnlyLoopback.Should().BeTrue();
        policy.IsAllowed(IPAddress.Loopback).Should().BeTrue();
        policy.IsAllowed(IPAddress.Parse("192.168.21.1")).Should().BeFalse(
            "pusta lista nie może znaczyć \"wpuszczaj wszystkich\"");
    }

    [Fact]
    public void IsAllowed_WithoutARemoteAddress_Rejects()
    {
        NetworkAccessPolicy.Create(ConfiguredNetworks).IsAllowed(null).Should().BeFalse();
    }

    [Fact]
    public void Create_AcceptsABareAddressAsASingleHost()
    {
        var policy = NetworkAccessPolicy.Create(["192.168.21.7"]);

        policy.IsAllowed(IPAddress.Parse("192.168.21.7")).Should().BeTrue();
        policy.IsAllowed(IPAddress.Parse("192.168.21.8")).Should().BeFalse();
    }

    [Theory]
    [InlineData("192.168.21.0/33")]
    [InlineData("192.168.21.0/abc")]
    [InlineData("to nie jest adres")]
    [InlineData("999.1.1.1/24")]
    public void Create_ReportsEntriesItCouldNotParse(string entry)
    {
        var policy = NetworkAccessPolicy.Create([entry]);

        policy.InvalidEntries.Should().ContainSingle().Which.Should().Be(entry);
        policy.AllowedNetworks.Should().BeEmpty();

        // Zły wpis nie może po cichu otworzyć dostępu.
        policy.AllowsOnlyLoopback.Should().BeTrue();
    }

    [Fact]
    public void Create_IgnoresBlankEntriesFromTheTextBox()
    {
        var policy = NetworkAccessPolicy.Create(["192.168.21.0/24", "", "   ", null]);

        policy.AllowedNetworks.Should().ContainSingle().Which.Should().Be("192.168.21.0/24");
        policy.InvalidEntries.Should().BeEmpty();
    }

    [Fact]
    public void Create_HandlesPrefixesThatDoNotFallOnAByteBoundary()
    {
        var policy = NetworkAccessPolicy.Create(["192.168.16.0/20"]);

        policy.IsAllowed(IPAddress.Parse("192.168.16.1")).Should().BeTrue();
        policy.IsAllowed(IPAddress.Parse("192.168.31.254")).Should().BeTrue();
        policy.IsAllowed(IPAddress.Parse("192.168.32.1")).Should().BeFalse();
    }
}
