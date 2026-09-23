using System.Net;
using OrderSphere.Webhooks.Application.Security;

namespace OrderSphere.Webhooks.Tests.Security;

public sealed class WebhookTargetPolicyTests
{
    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://hooks.partner.example:8443/in?x=1")]
    [InlineData("https://93.184.215.14/hook")]
    [InlineData("https://[2606:4700:4700::1111]/hook")]
    public void IsAllowedUrl_AcceptsPublicHttpsTargets(string url)
    {
        WebhookTargetPolicy.IsAllowedUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("http://example.com/hook")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://ordersphere-catalog/api")]
    [InlineData("https://api.localhost/")]
    [InlineData("https://printer.local/")]
    [InlineData("https://metadata.google.internal/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.1.2.3/")]
    [InlineData("https://172.16.0.1/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://100.64.0.1/")]
    [InlineData("https://0.0.0.0/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[fd00::1]/")]
    [InlineData("https://[fe80::1]/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    public void IsAllowedUrl_RejectsNonHttpsAndInternalTargets(string? url)
    {
        WebhookTargetPolicy.IsAllowedUrl(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("168.63.129.16")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:0a00:0001::1")]
    public void IsBlockedAddress_BlocksInternalRanges(string address)
    {
        WebhookTargetPolicy.IsBlockedAddress(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsBlockedAddress_AllowsPublicAddresses(string address)
    {
        WebhookTargetPolicy.IsBlockedAddress(IPAddress.Parse(address)).Should().BeFalse();
    }
}
