using Cfp.Application.Auditing;
using Cfp.Domain.Notifications;

namespace Cfp.Application.Abstractions;

public interface IEmailCampaignStore
{
    Task<Versioned<EmailCampaign>?> GetAsync(
        string conferenceId,
        string campaignId,
        CancellationToken cancellationToken);

    Task<Versioned<EmailCampaign>> CreatePreviewAsync(
        EmailCampaign campaign,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<Versioned<EmailCampaign>> ConfirmAndEnqueueAsync(
        EmailCampaign campaign,
        string expectedEtag,
        IReadOnlyList<EmailOutboxItem> outboxItems,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
