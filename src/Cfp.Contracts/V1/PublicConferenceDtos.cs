using MemoryPack;

namespace Cfp.Contracts.V1;

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicConferenceSummaryDto
{
    [MemoryPackOrder(0)]
    public string Slug { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string CfpAvailability { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicConferencePageDto
{
    [MemoryPackOrder(0)]
    public List<PublicConferenceSummaryDto> Conferences { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicConferenceDto
{
    [MemoryPackOrder(0)]
    public string Slug { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string EndsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string CfpAvailability { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public string CfpOpensAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(8)]
    public string CfpClosesAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(9)]
    public string ConferenceId { get; set; } = string.Empty;

    [MemoryPackOrder(10)]
    public bool PublicShowcaseEnabled { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ApiErrorDto
{
    [MemoryPackOrder(0)]
    public string Code { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Message { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string? CorrelationId { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class CreateConferenceRequestDto
{
    [MemoryPackOrder(0)]
    public string Slug { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string EndsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string CfpOpensAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public string CfpClosesAtUtc { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class UpdateConferenceRequestDto
{
    [MemoryPackOrder(0)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string EndsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string CfpOpensAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string CfpClosesAtUtc { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ArchiveConferenceRequestDto
{
    [MemoryPackOrder(0)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class CreatedConferenceDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Slug { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string LifecycleState { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string Visibility { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ProposalFormFieldDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Label { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Kind { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public bool IsRequired { get; set; }

    [MemoryPackOrder(4)]
    public int? MaximumLength { get; set; }

    [MemoryPackOrder(5)]
    public List<string>? Options { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicProposalTypeDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public int DurationMinutes { get; set; }

    [MemoryPackOrder(4)]
    public int FormVersion { get; set; }

    [MemoryPackOrder(5)]
    public bool IsAcceptingSubmissions { get; set; }

    [MemoryPackOrder(6)]
    public List<ProposalFormFieldDto> Fields { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicProposalTypesDto
{
    [MemoryPackOrder(0)]
    public List<PublicProposalTypeDto> ProposalTypes { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SaveProposalTypeRequestDto
{
    [MemoryPackOrder(0)]
    public string? ProposalTypeId { get; set; }

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public int DurationMinutes { get; set; }

    [MemoryPackOrder(4)]
    public bool IsAcceptingSubmissions { get; set; }

    [MemoryPackOrder(5)]
    public bool IsPublic { get; set; } = true;

    [MemoryPackOrder(6)]
    public List<ProposalFormFieldDto> Fields { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SavedProposalTypeDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public int FormVersion { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedProposalTypeDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public int DurationMinutes { get; set; }

    [MemoryPackOrder(4)]
    public int FormVersion { get; set; }

    [MemoryPackOrder(5)]
    public bool IsAcceptingSubmissions { get; set; }

    [MemoryPackOrder(6)]
    public bool IsPublic { get; set; }

    [MemoryPackOrder(7)]
    public string ETag { get; set; } = string.Empty;

    [MemoryPackOrder(8)]
    public List<ProposalFormFieldDto> Fields { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedProposalTypesDto
{
    [MemoryPackOrder(0)]
    public List<ManagedProposalTypeDto> ProposalTypes { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedConferenceDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Slug { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string Description { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string LifecycleState { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string Visibility { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string CfpAvailability { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(8)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(9)]
    public string EndsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(10)]
    public string CfpOpensAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(11)]
    public string CfpClosesAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(12)]
    public string ETag { get; set; } = string.Empty;

    [MemoryPackOrder(13)]
    public string Role { get; set; } = string.Empty;

    [MemoryPackOrder(14)]
    public bool PublicShowcaseEnabled { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SetCfpPublicationStateRequestDto
{
    [MemoryPackOrder(0)]
    public string State { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SetPublicShowcaseRequestDto
{
    [MemoryPackOrder(0)]
    public bool Enabled { get; set; }

    [MemoryPackOrder(1)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedConferenceSummaryDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Slug { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string LifecycleState { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string Visibility { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string CfpAvailability { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(8)]
    public string Role { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedConferencesDto
{
    [MemoryPackOrder(0)]
    public List<ManagedConferenceSummaryDto> Conferences { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ProposalDraftRequestDto
{
    [MemoryPackOrder(0)]
    public string ProposalTypeId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public Dictionary<string, string> Answers { get; set; } = new(StringComparer.Ordinal);
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class UpdateProposalRequestDto
{
    [MemoryPackOrder(0)]
    public Dictionary<string, string> Answers { get; set; } = new(StringComparer.Ordinal);
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ProposalDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string ConferenceId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string ProposalTypeId { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public int FormVersion { get; set; }

    [MemoryPackOrder(4)]
    public Dictionary<string, string> Answers { get; set; } = new(StringComparer.Ordinal);

    [MemoryPackOrder(5)]
    public string Status { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string? SubmittedAtUtc { get; set; }

    [MemoryPackOrder(7)]
    public string UpdatedAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(8)]
    public string ETag { get; set; } = string.Empty;

    [MemoryPackOrder(9)]
    public List<ProposalFormFieldDto> FormFields { get; set; } = [];

    [MemoryPackOrder(10)]
    public string ConferenceSlug { get; set; } = string.Empty;

    [MemoryPackOrder(11)]
    public string ConferenceTitle { get; set; } = string.Empty;

    [MemoryPackOrder(12)]
    public bool PublicationConsentConfirmed { get; set; }

    [MemoryPackOrder(13)]
    public string PublicationState { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedProposalDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string ConferenceId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string ProposalTypeId { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public int FormVersion { get; set; }

    [MemoryPackOrder(4)]
    public Dictionary<string, string> Answers { get; set; } = new(StringComparer.Ordinal);

    [MemoryPackOrder(5)]
    public string Status { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string? SubmittedAtUtc { get; set; }

    [MemoryPackOrder(7)]
    public string? DecisionReason { get; set; }

    [MemoryPackOrder(8)]
    public string ETag { get; set; } = string.Empty;

    [MemoryPackOrder(9)]
    public bool PublicationConsentConfirmed { get; set; }

    [MemoryPackOrder(10)]
    public string PublicationState { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ReviewSummaryDto
{
    [MemoryPackOrder(0)]
    public string ReviewerUserId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public int Score { get; set; }

    [MemoryPackOrder(2)]
    public string Comment { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string SubmittedAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string ETag { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedProposalDetailsDto
{
    [MemoryPackOrder(0)]
    public ManagedProposalDto Proposal { get; set; } = new();

    [MemoryPackOrder(1)]
    public List<ReviewSummaryDto> Reviews { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedProposalsDto
{
    [MemoryPackOrder(0)]
    public List<ManagedProposalDto> Proposals { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class MyProposalsDto
{
    [MemoryPackOrder(0)]
    public List<ProposalDto> Proposals { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class AssignReviewerRequestDto
{
    [MemoryPackOrder(0)]
    public string ProposalId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string ReviewerUserId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ReviewSubmissionRequestDto
{
    [MemoryPackOrder(0)]
    public int Score { get; set; }

    [MemoryPackOrder(1)]
    public string Comment { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public bool HasConflictOfInterest { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ReviewSubmissionResponseDto
{
    [MemoryPackOrder(0)]
    public bool ConflictDeclared { get; set; }

    [MemoryPackOrder(1)]
    public string? ProposalId { get; set; }

    [MemoryPackOrder(2)]
    public int? Score { get; set; }

    [MemoryPackOrder(3)]
    public string? Comment { get; set; }

    [MemoryPackOrder(4)]
    public string? ETag { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ReviewTaskDto
{
    [MemoryPackOrder(0)]
    public string ConferenceId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string ProposalId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public Dictionary<string, string> Answers { get; set; } = new(StringComparer.Ordinal);

    [MemoryPackOrder(3)]
    public string ProposalStatus { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public bool HasConflictOfInterest { get; set; }

    [MemoryPackOrder(5)]
    public int? ReviewScore { get; set; }

    [MemoryPackOrder(6)]
    public string? ReviewComment { get; set; }

    [MemoryPackOrder(7)]
    public string ProposalETag { get; set; } = string.Empty;

    [MemoryPackOrder(8)]
    public string? ReviewETag { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ReviewTasksDto
{
    [MemoryPackOrder(0)]
    public List<ReviewTaskDto> Tasks { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ProposalDecisionRequestDto
{
    [MemoryPackOrder(0)]
    public bool Accepted { get; set; }

    [MemoryPackOrder(1)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ProposalDecisionResponseDto
{
    [MemoryPackOrder(0)]
    public string ProposalId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Status { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string? DecisionReason { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SetProposalPublicationConsentRequestDto
{
    [MemoryPackOrder(0)]
    public bool Confirmed { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SetProposalPublicationRequestDto
{
    [MemoryPackOrder(0)]
    public bool Published { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicProposalDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Abstract { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string Speakers { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string ConferenceTitle { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicProposalsDto
{
    [MemoryPackOrder(0)]
    public List<PublicProposalDto> Proposals { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ScheduleRoomDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ScheduleTrackDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ScheduleSlotInputDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string ProposalId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string RoomId { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string? TrackId { get; set; }

    [MemoryPackOrder(4)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string EndsAtUtc { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SaveScheduleDraftRequestDto
{
    [MemoryPackOrder(0)]
    public List<ScheduleRoomDto> Rooms { get; set; } = [];

    [MemoryPackOrder(1)]
    public List<ScheduleTrackDto> Tracks { get; set; } = [];

    [MemoryPackOrder(2)]
    public List<ScheduleSlotInputDto> Slots { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ManagedScheduleDto
{
    [MemoryPackOrder(0)]
    public int Revision { get; set; }

    [MemoryPackOrder(1)]
    public List<ScheduleRoomDto> Rooms { get; set; } = [];

    [MemoryPackOrder(2)]
    public List<ScheduleTrackDto> Tracks { get; set; } = [];

    [MemoryPackOrder(3)]
    public List<ScheduleSlotInputDto> Slots { get; set; } = [];

    [MemoryPackOrder(4)]
    public string DraftETag { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public int? PublishedRevision { get; set; }

    [MemoryPackOrder(6)]
    public string? PublicationETag { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicScheduleSessionDto
{
    [MemoryPackOrder(0)]
    public string SessionId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Title { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Abstract { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string Speakers { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string RoomName { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string? TrackName { get; set; }

    [MemoryPackOrder(6)]
    public string StartsAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public string EndsAtUtc { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PublicScheduleDto
{
    [MemoryPackOrder(0)]
    public string ConferenceTitle { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string TimeZoneId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public List<PublicScheduleSessionDto> Sessions { get; set; } = [];
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ConferenceMemberDto
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Role { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public bool IsActive { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ConferenceMembersDto
{
    [MemoryPackOrder(0)]
    public List<ConferenceMemberDto> Members { get; set; } = [];

    [MemoryPackOrder(1)]
    public string ConferenceETag { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SetConferenceMemberRequestDto
{
    [MemoryPackOrder(0)]
    public string Role { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class ConferenceMemberUpdatedDto
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Role { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string ConferenceETag { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class EmailOutboxSummaryDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string RecipientUserId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Category { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string TemplateId { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string Status { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string? ProviderMessageId { get; set; }

    [MemoryPackOrder(6)]
    public string CreatedAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public string? StatusReason { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class EmailOutboxHistoryDto
{
    [MemoryPackOrder(0)]
    public List<EmailOutboxSummaryDto> Items { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class AuditEventDto
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string ActorUserId { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Operation { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string TargetId { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string OccurredAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string Summary { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class AuditEventsDto
{
    [MemoryPackOrder(0)]
    public List<AuditEventDto> Events { get; set; } = [];

    [MemoryPackOrder(1)]
    public string? ContinuationToken { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class PreviewTargetedEmailRequestDto
{
    [MemoryPackOrder(0)]
    public List<string> ProposalStatuses { get; set; } = [];

    [MemoryPackOrder(1)]
    public string Subject { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string PlainTextContent { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class EmailCampaignPreviewDto
{
    [MemoryPackOrder(0)]
    public string CampaignId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public int EligibleRecipientCount { get; set; }

    [MemoryPackOrder(2)]
    public int ExcludedRecipientCount { get; set; }

    [MemoryPackOrder(3)]
    public string Subject { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public string PlainTextContent { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public string ExpiresAtUtc { get; set; } = string.Empty;

    [MemoryPackOrder(6)]
    public string ETag { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public List<string> ProposalStatuses { get; set; } = [];

    [MemoryPackOrder(8)]
    public string SenderAddress { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SendTargetedEmailRequestDto
{
    [MemoryPackOrder(0)]
    public string CampaignId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class EmailCampaignSendResultDto
{
    [MemoryPackOrder(0)]
    public string CampaignId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string State { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public int RecipientCount { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class SpeakerProfileDto
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string DisplayName { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Biography { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public string? Email { get; set; }

    [MemoryPackOrder(4)]
    public bool EmailVerified { get; set; }

    [MemoryPackOrder(5)]
    public bool ConferenceOperationsOptIn { get; set; }

    [MemoryPackOrder(6)]
    public string ETag { get; set; } = string.Empty;

    [MemoryPackOrder(7)]
    public bool EmailSuppressed { get; set; }
}

[MemoryPackable(SerializeLayout.Explicit)]
public partial class UpdateSpeakerProfileRequestDto
{
    [MemoryPackOrder(0)]
    public string DisplayName { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Biography { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public bool ConferenceOperationsOptIn { get; set; }
}
