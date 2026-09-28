using Azure.Identity;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Conferences;
using Cfp.Application.Identity;
using Cfp.Application.Notifications;
using Cfp.Application.PublicConferences;
using Cfp.Application.ProposalTypes;
using Cfp.Application.Scheduling;
using Cfp.Infrastructure.Cosmos;
using Cfp.Infrastructure.Email;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cfp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCfpInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<CosmosClient>(_ =>
        {
            var connectionString =
                configuration["Cosmos:ConnectionString"] ??
                configuration["CosmosConnection"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return new CosmosClient(connectionString);
            }

            var endpoint = configuration["Cosmos:Endpoint"];
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
                endpointUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    "Cosmos:Endpoint must be configured as an HTTPS Cosmos DB endpoint.");
            }

            return new CosmosClient(endpointUri.ToString(), new DefaultAzureCredential());
        });
        services.AddSingleton<IPublicConferenceReader, CosmosPublicConferenceReader>();
        services.AddSingleton<CosmosConferenceManagementStore>();
        services.AddSingleton<IConferenceManagementStore>(
            serviceProvider => serviceProvider.GetRequiredService<CosmosConferenceManagementStore>());
        services.AddSingleton<IConferenceMembershipReader>(
            serviceProvider => serviceProvider.GetRequiredService<CosmosConferenceManagementStore>());
        services.AddSingleton<IConferenceMembershipManagementStore>(
            serviceProvider => serviceProvider.GetRequiredService<CosmosConferenceManagementStore>());
        services.AddSingleton<IConferenceLifecycleStore>(
            serviceProvider => serviceProvider.GetRequiredService<CosmosConferenceManagementStore>());
        services.AddSingleton<IManagedConferenceReader>(
            serviceProvider => serviceProvider.GetRequiredService<CosmosConferenceManagementStore>());
        services.AddSingleton<IUserIdentityDirectory, CosmosUserIdentityDirectory>();
        services.AddSingleton<IUserProfileStore, CosmosUserProfileStore>();
        services.AddSingleton<IProposalTypeStore, CosmosProposalTypeStore>();
        services.AddSingleton<IProposalWorkflowStore, CosmosProposalWorkflowStore>();
        services.AddSingleton<IReviewWorkflowStore, CosmosReviewWorkflowStore>();
        services.AddSingleton<IScheduleStore, CosmosScheduleStore>();
        services.AddSingleton<IAuditEventReader, CosmosAuditEventReader>();
        services.AddSingleton<IEmailOutboxStore, CosmosEmailOutboxStore>();
        services.AddSingleton<IEmailCampaignStore, CosmosEmailCampaignStore>();
        services.AddSingleton<IEmailDeliveryDirectory, CosmosEmailDeliveryDirectory>();
        services.AddSingleton<IEmailRecipientPolicy, ProfileEmailRecipientPolicy>();
        services.AddSingleton<IEmailSender, AzureCommunicationEmailSender>();

        return services;
    }
}
