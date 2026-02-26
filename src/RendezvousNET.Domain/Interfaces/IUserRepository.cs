using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User> AddAsync(User user, CancellationToken ct = default);
}
