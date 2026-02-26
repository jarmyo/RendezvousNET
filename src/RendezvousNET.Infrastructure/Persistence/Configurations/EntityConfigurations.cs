using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RendezvousNET.Domain.Entities;

namespace RendezvousNET.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        builder.HasIndex(u => u.Email).IsUnique();
    }
}

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Description).HasMaxLength(500);
        builder.Property(m => m.Location).IsRequired().HasMaxLength(300);
        builder.Property(m => m.InviteCode).IsRequired().HasMaxLength(8).IsFixedLength();
        builder.HasIndex(m => m.InviteCode).IsUnique();

        builder.HasOne(m => m.Host)
            .WithMany(u => u.HostedMeetings)
            .HasForeignKey(m => m.HostId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1-to-N: one Meeting has many MeetingParticipants
        builder.HasMany(m => m.Participants)
            .WithOne(p => p.Meeting)
            .HasForeignKey(p => p.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingParticipantConfiguration : IEntityTypeConfiguration<MeetingParticipant>
{
    public void Configure(EntityTypeBuilder<MeetingParticipant> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.GuestDisplayName).HasMaxLength(100);

        builder.HasOne(p => p.User)
            .WithMany(u => u.Participations)
            .HasForeignKey(p => p.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // 1-to-1: one participant has one trusted friend
        builder.HasOne(p => p.TrustedFriend)
            .WithOne(tf => tf.Participant)
            .HasForeignKey<TrustedFriend>(tf => tf.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrustedFriendConfiguration : IEntityTypeConfiguration<TrustedFriend>
{
    public void Configure(EntityTypeBuilder<TrustedFriend> builder)
    {
        builder.HasKey(tf => tf.Id);
        builder.Property(tf => tf.Email).IsRequired().HasMaxLength(256);
        builder.Property(tf => tf.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(tf => tf.DeviceToken).HasMaxLength(512);
    }
}
