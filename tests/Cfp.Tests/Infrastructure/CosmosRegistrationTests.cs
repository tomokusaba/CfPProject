using Cfp.Infrastructure;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cfp.Tests.Infrastructure;

public sealed class CosmosRegistrationTests
{
    [Fact]
    public void AspireConnectionString_RegistersCosmosClientWithoutAzureCredential()
    {
        var emulatorKey = Convert.ToBase64String(Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosConnection"] =
                    $"AccountEndpoint=https://localhost:8081/;AccountKey={emulatorKey};"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddCfpInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CosmosClient>();

        Assert.Equal(new Uri("https://localhost:8081/"), client.Endpoint);
    }
}
