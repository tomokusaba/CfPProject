using Cfp.Functions.Http;

namespace Cfp.Tests.Functions;

public sealed class PublicShareMetadataTests
{
    [Fact]
    public void CreateHtml_EncodesUserSuppliedMetadataAndUrls()
    {
        var html = PublicShareMetadataFunctions.CreateHtml(
            "<script>alert(1)</script>",
            "\" onload=\"alert(2)&",
            "https://example.test/share?x=\"<&",
            "https://example.test/c/example");

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot; onload=&quot;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
        Assert.Contains("https://example.test/c/example", html, StringComparison.Ordinal);
    }
}
