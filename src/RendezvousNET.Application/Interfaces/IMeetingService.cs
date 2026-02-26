using RendezvousNET.Application.Common;
using RendezvousNET.Shared.DTOs.Meetings;

namespace RendezvousNET.Application.Interfaces;

public interface IMeetingService
{
    Task<Result<MeetingDto>> CreateAsync(CreateMeetingRequestDto request, Guid hostId, CancellationToken cancellationToken = default);
    Task<Result<MeetingDto>> GetByIdAsync(Guid meetingId, CancellationToken cancellationToken = default);
    Task<Result<MeetingDto>> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<MeetingSummaryDto>>> GetByHostAsync(Guid hostId, CancellationToken cancellationToken = default);
    Task<Result> CancelAsync(Guid meetingId, Guid requestingUserId, CancellationToken cancellationToken = default);
}
