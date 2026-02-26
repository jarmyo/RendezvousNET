using Microsoft.EntityFrameworkCore;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Domain.Interfaces;
using RendezvousNET.Infrastructure.Data;

namespace RendezvousNET.Infrastructure.Repositories;

public class MeetingRepository(AppDbContext db) : IMeetingRepository
{
    public Task<Meeting?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Meetings.Include(m => m.Host).Include(m => m.Participants).ThenInclude(p => p.TrustedFriend)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<Meeting?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default) =>
        db.Meetings.Include(m => m.Host).Include(m => m.Participants).ThenInclude(p => p.TrustedFriend)
            .FirstOrDefaultAsync(m => m.InviteCode == inviteCode, ct);

    public async Task<IEnumerable<Meeting>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default) =>
        await db.Meetings.Include(m => m.Participants).Where(m => m.HostId == hostId).ToListAsync(ct);

    public async Task<Meeting> AddAsync(Meeting meeting, CancellationToken ct = default)
    {
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync(ct);
        return meeting;
    }

    public async Task UpdateAsync(Meeting meeting, CancellationToken ct = default)
    {
        db.Meetings.Update(meeting);
        await db.SaveChangesAsync(ct);
    }
}
