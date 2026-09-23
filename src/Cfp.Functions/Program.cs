using Cfp.Application.Authorization;
using Cfp.Application.Auditing;
using Cfp.Application.Conferences;
using Cfp.Application.Identity;
using Cfp.Application.Notifications;
using Cfp.Application.ProposalTypes;
using Cfp.Application.Proposals;
using Cfp.Application.Reviews;
using Cfp.Application.Scheduling;
using Cfp.Application.Profiles;
using Cfp.Application.PublicConferences;
using Cfp.Functions.Security;
using Cfp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddCfpInfrastructure(context.Configuration);
        services.AddSingleton<ActorResolutionService>();
        services.AddSingleton<ConferenceAuthorizationService>();
        services.AddSingleton<CreateConferenceHandler>();
        services.AddSingleton<PublishConferenceHandler>();
        services.AddSingleton<UpdateConferenceHandler>();
        services.AddSingleton<ArchiveConferenceHandler>();
        services.AddSingleton<SetCfpPublicationStateHandler>();
        services.AddSingleton<SetPublicShowcaseHandler>();
        services.AddSingleton<ListConferenceAuditEventsHandler>();
        services.AddSingleton<ManageConferenceMembershipHandler>();
        services.AddSingleton<EasyAuthPrincipalReader>();
        services.AddSingleton<GetPublicProposalTypesHandler>();
        services.AddSingleton<ListPublicProposalsHandler>();
        services.AddSingleton<GetPublicProposalHandler>();
        services.AddSingleton<ListManagedConferencesHandler>();
        services.AddSingleton<ListManagedProposalTypesHandler>();
        services.AddSingleton<SaveProposalTypeHandler>();
        services.AddSingleton<SaveProposalDraftHandler>();
        services.AddSingleton<UpdateProposalHandler>();
        services.AddSingleton<SubmitProposalHandler>();
        services.AddSingleton<WithdrawProposalHandler>();
        services.AddSingleton<ListMyProposalsHandler>();
        services.AddSingleton<GetMyProposalHandler>();
        services.AddSingleton<SetProposalPublicationConsentHandler>();
        services.AddSingleton<SetProposalPublicationStateHandler>();
        services.AddSingleton<ListConferenceProposalsHandler>();
        services.AddSingleton<GetConferenceProposalHandler>();
        services.AddSingleton<GetSpeakerProfileHandler>();
        services.AddSingleton<UpdateSpeakerProfileHandler>();
        services.AddSingleton<AssignReviewersHandler>();
        services.AddSingleton<SubmitReviewHandler>();
        services.AddSingleton<DecideProposalHandler>();
        services.AddSingleton<ListMyReviewAssignmentsHandler>();
        services.AddSingleton<SaveScheduleDraftHandler>();
        services.AddSingleton<PublishScheduleHandler>();
        services.AddSingleton<GetPublicScheduleHandler>();
        services.AddSingleton<EmailOutboxDispatchHandler>();
        services.AddSingleton<EmailDeliveryReportHandler>();
        services.AddSingleton<PreviewTargetedEmailHandler>();
        services.AddSingleton<SendTargetedEmailHandler>();
        services.AddSingleton<ListPublicConferencesHandler>();
        services.AddSingleton<GetPublicConferenceHandler>();
    })
    .Build();

host.Run();
