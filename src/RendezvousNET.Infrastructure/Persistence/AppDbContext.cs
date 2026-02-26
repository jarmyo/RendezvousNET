using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core DbContext. Inherits from IdentityDbContext so ASP.NET Core Identity
/// tables live in the same database schema.
/// </summary>
public sealed class AppDbContext : IdentityDbContext<AppUser>, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // IAppDbContext members — backed by EF Core DbSet<T> instances
    IQueryable<User> IAppDbContext.Users => DomainUsers;
    IQueryable<Meeting> IAppDbContext.Meetings => MeetingSet;
    IQueryable<MeetingParticipant> IAppDbContext.MeetingParticipants => ParticipantSet;
    IQueryable<TrustedFriend> IAppDbContext.TrustedFriends => TrustedFriendSet;

    EntityEntry<TEntity> IAppDbContext.Add<TEntity>(TEntity entity) => Add(entity);
    EntityEntry IAppDbContext.Remove(object entity) => Remove(entity);

    // Internal DbSets
    internal DbSet<User> DomainUsers => Set<User>();
    internal DbSet<Meeting> MeetingSet => Set<Meeting>();
    internal DbSet<MeetingParticipant> ParticipantSet => Set<MeetingParticipant>();
    internal DbSet<TrustedFriend> TrustedFriendSet => Set<TrustedFriend>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

/// <summary>ASP.NET Core Identity user linked to the domain <see cref="User"/>.</summary>
public sealed class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
