using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Domain.Interfaces;

public interface IMeetingRepository
{
    Task<Meeting?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Meeting?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default);
    Task<IEnumerable<Meeting>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default);
    Task<Meeting> AddAsync(Meeting meeting, CancellationToken ct = default);
    Task UpdateAsync(Meeting meeting, CancellationToken ct = default);
}
