using FluentAssertions;
using OrderSphere.Bff.Auth;
using Xunit;

namespace OrderSphere.Bff.Tests;

/// <summary>
/// <c>/bff/login?returnUrl=</c> must only redirect within this origin after sign-in;
/// anything else falls back to the start page.
/// </summary>
public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/products/some-slug")]
    [InlineData("/checkout?step=2#payment")]
    public void Sanitize_KeepsLocalPaths(string returnUrl)
    {
        LocalReturnUrl.Sanitize(returnUrl).Should().Be(returnUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/\t/evil.example")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    public void Sanitize_ReplacesNonLocalTargetsWithRoot(string? returnUrl)
    {
        LocalReturnUrl.Sanitize(returnUrl).Should().Be("/");
    }
}
