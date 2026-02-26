using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Domain.Interfaces;

public interface IParticipantRepository
{
    Task<MeetingParticipant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<MeetingParticipant>> GetByMeetingIdAsync(Guid meetingId, CancellationToken ct = default);
    Task<MeetingParticipant> AddAsync(MeetingParticipant participant, CancellationToken ct = default);
    Task UpdateAsync(MeetingParticipant participant, CancellationToken ct = default);
}
