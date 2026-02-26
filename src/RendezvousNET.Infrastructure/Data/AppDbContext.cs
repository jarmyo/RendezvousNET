using Microsoft.EntityFrameworkCore;
using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingParticipant> MeetingParticipants => Set<MeetingParticipant>();
    public DbSet<TrustedFriend> TrustedFriends => Set<TrustedFriend>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<Meeting>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.InviteCode).IsUnique();
            e.Property(m => m.Title).HasMaxLength(200).IsRequired();
            e.Property(m => m.Location).HasMaxLength(500).IsRequired();
            e.Property(m => m.InviteCode).HasMaxLength(20).IsRequired();
            e.HasOne(m => m.Host)
                .WithMany(u => u.HostedMeetings)
                .HasForeignKey(m => m.HostId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(m => m.Participants)
                .WithOne(p => p.Meeting)
                .HasForeignKey(p => p.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingParticipant>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.DisplayName).HasMaxLength(100).IsRequired();
            e.HasOne(p => p.User)
                .WithMany(u => u.Participations)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            e.HasOne(p => p.TrustedFriend)
                .WithOne(tf => tf.Participant)
                .HasForeignKey<TrustedFriend>(tf => tf.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrustedFriend>(e =>
        {
            e.HasKey(tf => tf.Id);
            e.Property(tf => tf.Name).HasMaxLength(100).IsRequired();
            e.Property(tf => tf.Email).HasMaxLength(256).IsRequired();
            e.Property(tf => tf.Phone).HasMaxLength(20);
        });
    }
}
