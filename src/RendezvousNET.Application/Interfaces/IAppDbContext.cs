using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Application.Interfaces;

/// <summary>Abstraction over the data persistence layer.</summary>
public interface IAppDbContext
{
    System.Linq.IQueryable<User> Users { get; }
    System.Linq.IQueryable<Meeting> Meetings { get; }
    System.Linq.IQueryable<MeetingParticipant> MeetingParticipants { get; }
    System.Linq.IQueryable<TrustedFriend> TrustedFriends { get; }

    System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default);
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Add<TEntity>(TEntity entity) where TEntity : class;
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Remove(object entity);
}
