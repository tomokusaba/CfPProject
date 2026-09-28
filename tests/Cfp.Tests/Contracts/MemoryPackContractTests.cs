using Cfp.Contracts.V1;
using MemoryPack;

namespace Cfp.Tests.Contracts;

public sealed class MemoryPackContractTests
{
    [Fact]
    public void PublicConferencePage_RoundTripsWithMemoryPack()
    {
        var original = new PublicConferencePageDto
        {
            Conferences =
            [
                new PublicConferenceSummaryDto
                {
                    Slug = "example-conf",
                    Title = "Example Conference",
                    StartsAtUtc = "2026-11-01T00:00:00.0000000+00:00",
                    TimeZoneId = "Asia/Tokyo",
                    CfpAvailability = "Open"
                }
            ],
            ContinuationToken = "opaque-cursor"
        };

        var decoded = MemoryPackSerializer.Deserialize<PublicConferencePageDto>(
            MemoryPackSerializer.Serialize(original));

        Assert.NotNull(decoded);
        Assert.Equal(original.ContinuationToken, decoded.ContinuationToken);
        Assert.Equal(original.Conferences[0].Slug, decoded.Conferences[0].Slug);
        Assert.Equal(original.Conferences[0].CfpAvailability, decoded.Conferences[0].CfpAvailability);
    }
}
