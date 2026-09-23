using System.Text;
using System.Text.Json;
using Cfp.Functions.Security;

namespace Cfp.Tests.Identity;

public sealed class EasyAuthPrincipalReaderTests
{
    [Fact]
    public void ParseHeader_UsesVerifiedIssuerAndSubjectClaims()
    {
        var header = Encode(new
        {
            auth_typ = "externalid",
            claims = new[]
            {
                new { typ = "iss", val = "https://tenant.example/" },
                new { typ = "sub", val = "stable-subject" },
                new { typ = "email", val = "speaker@example.test" }
            }
        });

        var identity = EasyAuthPrincipalReader.ParseHeader(header, "externalid");

        Assert.NotNull(identity);
        Assert.Equal("https://tenant.example/", identity.Issuer);
        Assert.Equal("stable-subject", identity.Subject);
    }

    [Theory]
    [InlineData("not-base64", "externalid")]
    [InlineData("", "externalid")]
    public void ParseHeader_RejectsMalformedOrEmptyHeader(string header, string provider)
    {
        Assert.Null(EasyAuthPrincipalReader.ParseHeader(header, provider));
    }

    [Fact]
    public void ParseHeader_RejectsWrongProviderAndEmailOnlyIdentities()
    {
        var wrongProvider = Encode(new
        {
            auth_typ = "other",
            claims = new[]
            {
                new { typ = "iss", val = "https://tenant.example/" },
                new { typ = "sub", val = "subject" }
            }
        });
        var emailOnly = Encode(new
        {
            auth_typ = "externalid",
            claims = new[] { new { typ = "email", val = "speaker@example.test" } }
        });

        Assert.Null(EasyAuthPrincipalReader.ParseHeader(wrongProvider, "externalid"));
        Assert.Null(EasyAuthPrincipalReader.ParseHeader(emailOnly, "externalid"));
    }

    private static string Encode<T>(T value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
}
