using WitcherHub.Infrastructure.Authentication;

namespace WitcherHub.Tests;

public class PublicBaseUrlTests
{
    [Theory]
    [InlineData("https://hub.netwitcher.com", "https://hub.netwitcher.com")]
    [InlineData("https://hub.netwitcher.com/", "https://hub.netwitcher.com")]
    [InlineData("https://hub.netwitcher.com///", "https://hub.netwitcher.com")]
    [InlineData("  https://hub.netwitcher.com  ", "https://hub.netwitcher.com")]
    [InlineData("http://localhost:5199", "http://localhost:5199")]
    public void KeepsAWellFormedUrlAndTrimsTrailingSlashes(string raw, string expected)
    {
        Assert.Equal(expected, PublicBaseUrl.Normalise(raw));
    }

    [Theory]
    // Railway displays hostnames without a scheme; pasting that form in produced a
    // link no mail client could follow.
    [InlineData("witcherhubdev-dev.up.railway.app", "https://witcherhubdev-dev.up.railway.app")]
    [InlineData("hub.netwitcher.com/", "https://hub.netwitcher.com")]
    public void SuppliesHttpsWhenTheSchemeIsMissing(string raw, string expected)
    {
        Assert.Equal(expected, PublicBaseUrl.Normalise(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("ftp://hub.netwitcher.com")]
    [InlineData("javascript:alert(1)")]
    public void RejectsValuesThatCannotProduceAWebLink(string? raw)
    {
        Assert.Null(PublicBaseUrl.Normalise(raw));
    }
}
