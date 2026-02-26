using Microsoft.EntityFrameworkCore;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Domain.Interfaces;
using RendezvousNET.Infrastructure.Data;

namespace RendezvousNET.Infrastructure.Repositories;

public class ParticipantRepository(AppDbContext db) : IParticipantRepository
{
    public Task<MeetingParticipant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.MeetingParticipants.Include(p => p.TrustedFriend).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IEnumerable<MeetingParticipant>> GetByMeetingIdAsync(Guid meetingId, CancellationToken ct = default) =>
        await db.MeetingParticipants.Include(p => p.TrustedFriend).Where(p => p.MeetingId == meetingId).ToListAsync(ct);

    public async Task<MeetingParticipant> AddAsync(MeetingParticipant participant, CancellationToken ct = default)
    {
        db.MeetingParticipants.Add(participant);
        await db.SaveChangesAsync(ct);
        return participant;
    }

    public async Task UpdateAsync(MeetingParticipant participant, CancellationToken ct = default)
    {
        db.MeetingParticipants.Update(participant);
        await db.SaveChangesAsync(ct);
    }
}
