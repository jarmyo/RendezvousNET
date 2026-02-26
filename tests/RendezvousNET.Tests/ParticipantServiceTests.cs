using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Application.Services;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Infrastructure.Persistence;
using RendezvousNET.Shared.DTOs.Notifications;
using RendezvousNET.Shared.DTOs.Participants;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Tests;

public sealed class ParticipantServiceTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task JoinMeetingAsync_WithValidInviteCode_CreatesParticipant()
    {
        await using var db = CreateDb(nameof(JoinMeetingAsync_WithValidInviteCode_CreatesParticipant));

        var host = new User { Id = Guid.NewGuid(), Email = "host@test.com", DisplayName = "Host" };
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Location = "Loc",
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1),
            InviteCode = "INVIT123",
            HostId = host.Id,
            Host = host,
            Status = MeetingStatus.Scheduled
        };
        var user = new User { Id = Guid.NewGuid(), Email = "u@test.com", DisplayName = "User A" };
        db.Add(host);
        db.Add(meeting);
        db.Add(user);
        await db.SaveChangesAsync();

        var notifMock = new Mock<INotificationService>();
        var service = new ParticipantService(db, notifMock.Object);

        var result = await service.JoinMeetingAsync(new JoinMeetingRequestDto { InviteCode = "INVIT123" }, user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ParticipantType.Should().Be(ParticipantType.RegisteredUser);
        result.Value.Status.Should().Be(ParticipantStatus.Pending);
    }

    [Fact]
    public async Task JoinMeetingAsync_WithInvalidInviteCode_ReturnsFailure()
    {
        await using var db = CreateDb(nameof(JoinMeetingAsync_WithInvalidInviteCode_ReturnsFailure));

        var notifMock = new Mock<INotificationService>();
        var service = new ParticipantService(db, notifMock.Object);

        var result = await service.JoinMeetingAsync(
            new JoinMeetingRequestDto { InviteCode = "XXXXXXXX" }, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("invite code");
    }

    [Fact]
    public async Task UpdateStatusAsync_ToDelayed_WithoutLocation_ReturnsFailure()
    {
        await using var db = CreateDb(nameof(UpdateStatusAsync_ToDelayed_WithoutLocation_ReturnsFailure));

        var host = new User { Id = Guid.NewGuid(), Email = "h@test.com", DisplayName = "H" };
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "T",
            Location = "L",
            ScheduledAt = DateTimeOffset.UtcNow,
            InviteCode = "AAAAAAAA",
            HostId = host.Id,
            Host = host
        };
        var participant = new MeetingParticipant
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting.Id,
            Meeting = meeting,
            Status = ParticipantStatus.Pending
        };
        db.Add(host);
        db.Add(meeting);
        db.Add(participant);
        await db.SaveChangesAsync();

        var notifMock = new Mock<INotificationService>();
        var service = new ParticipantService(db, notifMock.Object);

        var result = await service.UpdateStatusAsync(participant.Id,
            new UpdateParticipantStatusRequestDto { Status = ParticipantStatus.Delayed });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Latitude");
    }

    [Fact]
    public async Task UpdateStatusAsync_ToEmergency_NotifiesTrustedFriend()
    {
        await using var db = CreateDb(nameof(UpdateStatusAsync_ToEmergency_NotifiesTrustedFriend));

        var host = new User { Id = Guid.NewGuid(), Email = "h@test.com", DisplayName = "H" };
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "T",
            Location = "Danger Zone",
            ScheduledAt = DateTimeOffset.UtcNow,
            InviteCode = "AAAAAAAA",
            HostId = host.Id,
            Host = host
        };
        var participant = new MeetingParticipant
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting.Id,
            Meeting = meeting,
            Status = ParticipantStatus.Pending
        };
        var trustedFriend = new TrustedFriend
        {
            Id = Guid.NewGuid(),
            ParticipantId = participant.Id,
            Email = "friend@test.com",
            DisplayName = "Trusted Friend",
            Participant = participant
        };
        participant.TrustedFriend = trustedFriend;

        db.Add(host);
        db.Add(meeting);
        db.Add(participant);
        db.Add(trustedFriend);
        await db.SaveChangesAsync();

        var notifMock = new Mock<INotificationService>();
        notifMock.Setup(n => n.SendAsync(It.IsAny<SendNotificationRequestDto>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        var service = new ParticipantService(db, notifMock.Object);

        var result = await service.UpdateStatusAsync(participant.Id,
            new UpdateParticipantStatusRequestDto { Status = ParticipantStatus.Emergency });

        result.IsSuccess.Should().BeTrue();
        notifMock.Verify(
            n => n.SendAsync(
                It.Is<SendNotificationRequestDto>(r => r.RecipientEmail == "friend@test.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
