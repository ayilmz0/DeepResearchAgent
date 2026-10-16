
using DeepResearchAgent.Infrastructure.Services.Crawling;

namespace DeepResearchAgent.Tests;

public class CrawlerUrlValidatorTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://example.local")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://[::1]")]
    [InlineData("http://[fc00::1]")]
    [InlineData("http://[fe80::1]")]
    [InlineData("http://0.1.2.3")]
    [InlineData("http://100.64.0.1")]
    [InlineData("http://192.0.2.1")]
    [InlineData("http://198.18.0.1")]
    [InlineData("http://198.51.100.1")]
    [InlineData("http://203.0.113.1")]
    [InlineData("http://224.0.0.1")]
    [InlineData("http://255.255.255.255")]
    [InlineData("http://[2001:db8::1]")]
    [InlineData("http://[2002::1]")]
    [InlineData("http://[2001:1::1]")]
    [InlineData("http://[::ffff:127.0.0.1]")]
    public async Task ValidateAsync_UnsafeUrl_Throws(
        string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrawlerUrlValidator.ValidateAsync(url));
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("not-a-url")]
    public async Task ValidateAsync_InvalidSchemeOrUrl_Throws(
        string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrawlerUrlValidator.ValidateAsync(url));
    }

    [Theory]
    [InlineData("http://user:password@example.com")]
    [InlineData("https://user@example.com")]
    public async Task ValidateAsync_UrlContainsCredentials_Throws(
        string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrawlerUrlValidator.ValidateAsync(url));
    }

    [Fact]
    public async Task ResolveSafeAddressesAsync_LoopbackIp_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrawlerUrlValidator.ResolveSafeAddressesAsync(
                "127.0.0.1"));
    }

    [Fact]
    public async Task ResolveSafeAddressesAsync_PrivateIpv4_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrawlerUrlValidator.ResolveSafeAddressesAsync(
                "192.168.1.10"));
    }

    [Fact]
    public async Task ResolveSafeAddressesAsync_LoopbackIpv6_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrawlerUrlValidator.ResolveSafeAddressesAsync(
                "::1"));
    }
}
