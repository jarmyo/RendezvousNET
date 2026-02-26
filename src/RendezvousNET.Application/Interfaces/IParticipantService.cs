using RendezvousNET.Application.Common;
using RendezvousNET.Shared.DTOs.Participants;

namespace RendezvousNET.Application.Interfaces;

public interface IParticipantService
{
    /// <summary>Join a meeting via invite code. Works for both registered users and anonymous guests.</summary>
    Task<Result<MeetingParticipantDto>> JoinMeetingAsync(JoinMeetingRequestDto request, Guid? userId, CancellationToken cancellationToken = default);

    /// <summary>Update the real-time status of a participant (drives the color circle).</summary>
    Task<Result<ParticipantStatusUpdatedDto>> UpdateStatusAsync(Guid participantId, UpdateParticipantStatusRequestDto request, CancellationToken cancellationToken = default);

    Task<Result<MeetingParticipantDto>> GetByIdAsync(Guid participantId, CancellationToken cancellationToken = default);

    /// <summary>Trigger check-in request for all participants whose meeting starts now.</summary>
    Task ProcessCheckInRequestsAsync(CancellationToken cancellationToken = default);
}
