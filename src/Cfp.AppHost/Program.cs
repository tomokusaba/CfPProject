using Aspire.Hosting;
using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator(emulator => emulator.WithDataVolume());
var database = cosmos.AddCosmosDatabase("cfp");
database.AddContainer("conferenceData", "/conferenceId");
database.AddContainer("userProfiles", "/userId");
database.AddContainer("conferenceDirectory", "/slug");
database.AddContainer("identityDirectory", "/identityKey");
database.AddContainer("emailDeliveryDirectory", "/providerMessageId");
database.AddContainer("functionLeases", "/id");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

builder.AddAzureFunctionsProject<Projects.Cfp_Functions>("functions")
    .WithHostStorage(storage)
    .WithEnvironment("COSMOS_DATABASE_NAME", "cfp")
    .WithEnvironment("Cosmos__DatabaseName", "cfp")
    .WithReference(cosmos, "CosmosConnection")
    .WaitFor(cosmos)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Cfp_Web>("web")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithExternalHttpEndpoints();

builder.Build().Run();
