using System.Net;
using MediaForge.Services;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

public sealed class UrlSafetyTests(Workspace workspace) : IClassFixture<Workspace>
{
    [Fact(DisplayName = "Local website converts through HTTP and Pandoc")]
    public async Task Local_website_converts()
    {
        var urlFile = workspace.PathOf("website.url");
        var (url, server) = TestData.StartLocalWebsite();
        await File.WriteAllTextAsync(urlFile, url);
        var localService = new ConversionService(TestPaths.Tools, workspace.PathOf("temp-local"), UrlSafetyPolicy.AllowPrivateNetwork);

        var website = await localService.ConvertAsync(urlFile, new ConversionOptions("md"), workspace.Output);
        await server.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("MediaForge URL", await File.ReadAllTextAsync(website.FullPath), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "URL conversion blocks loopback targets by default (SSRF protection)")]
    public async Task Loopback_is_blocked_by_default()
    {
        var urlFile = workspace.PathOf("loopback.url");
        await File.WriteAllTextAsync(urlFile, "http://127.0.0.1:9/");
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => workspace.Service.ConvertAsync(urlFile, new ConversionOptions("md"), workspace.Output));
        Assert.Contains("bloqueado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "SSRF policy blocks loopback, private, link-local, CGNAT and reserved addresses")]
    public void Blocks_internal_addresses()
    {
        string[] blocked =
        [
            "0.0.0.0", "10.0.0.1", "100.64.0.1", "127.0.0.1", "169.254.169.254", "172.16.0.1",
            "172.31.255.255", "192.168.1.5", "224.0.0.1", "::1", "fe80::1", "fc00::1", "::ffff:10.0.0.1"
        ];
        Assert.All(blocked, address => Assert.True(UrlSafetyPolicy.IsBlockedAddress(IPAddress.Parse(address)), address));
    }

    [Fact(DisplayName = "SSRF policy keeps public addresses reachable")]
    public void Allows_public_addresses()
    {
        string[] allowed = ["1.1.1.1", "8.8.8.8", "172.32.0.1", "2606:4700:4700::1111"];
        Assert.All(allowed, address => Assert.False(UrlSafetyPolicy.IsBlockedAddress(IPAddress.Parse(address)), address));
    }

    [Fact(DisplayName = "SSRF policy rejects non-HTTP, local, metadata and oversized URLs")]
    public void Rejects_unsafe_urls()
    {
        string[] rejected =
        [
            "ftp://example.com/file", "file:///C:/Windows/win.ini", "http://localhost/admin", "http://printer.local/",
            "http://[::1]/", "http://169.254.169.254/latest/meta-data/", "http://2130706433/", "not a url",
            "https://example.com/" + new string('a', UrlSafetyPolicy.MaximumUrlLength)
        ];
        Assert.All(rejected, url => Assert.Throws<ArgumentException>(() => UrlSafetyPolicy.Default.ValidateUrl(url)));
    }

    [Fact(DisplayName = "SSRF policy accepts public HTTPS URLs")]
    public void Accepts_public_https_urls()
    {
        Assert.Equal("example.com", UrlSafetyPolicy.Default.ValidateUrl(" https://example.com/path?q=1 ").Host);
    }

    [Fact(DisplayName = "URL extraction supports Windows shortcuts and plain URLs")]
    public void Extracts_urls()
    {
        Assert.Equal("https://example.com/a", ConversionService.ExtractUrl("[InternetShortcut]\r\nURL=https://example.com/a\r\n"));
        Assert.Equal("https://example.com/b", ConversionService.ExtractUrl("  https://example.com/b \n"));
    }

    [Fact(DisplayName = "Tool diagnostics hide URLs and are capped in length")]
    public void Diagnostics_hide_urls_and_are_capped()
    {
        var diagnostic = ConversionService.CleanDiagnostic("Error: cannot open https://secret.example.com/a?token=123 \r\n  now");
        Assert.DoesNotContain("secret.example.com", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', diagnostic);
        Assert.Equal(600, ConversionService.CleanDiagnostic(new string('x', 5_000)).Length);
    }
}
