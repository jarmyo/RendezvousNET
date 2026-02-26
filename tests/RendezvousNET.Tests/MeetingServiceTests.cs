using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RendezvousNET.Application.Services;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Infrastructure.Persistence;
using RendezvousNET.Shared.DTOs.Meetings;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Tests;

/// <summary>
/// Unit tests for <see cref="MeetingService"/>.
/// Uses EF Core InMemory database for full async LINQ support.
/// </summary>
public sealed class MeetingServiceTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_WhenHostExists_ReturnsMeetingDto()
    {
        await using var db = CreateDb(nameof(CreateAsync_WhenHostExists_ReturnsMeetingDto));

        var host = new User { Id = Guid.NewGuid(), Email = "host@test.com", DisplayName = "Host User" };
        db.Add(host);
        await db.SaveChangesAsync();

        var service = new MeetingService(db);
        var request = new CreateMeetingRequestDto
        {
            Title = "Test Meeting",
            Location = "Office A",
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2)
        };

        var result = await service.CreateAsync(request, host.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Test Meeting");
        result.Value.InviteCode.Should().HaveLength(8);
        result.Value.Host.Id.Should().Be(host.Id);
    }

    [Fact]
    public async Task CreateAsync_WhenHostNotFound_ReturnsFailure()
    {
        await using var db = CreateDb(nameof(CreateAsync_WhenHostNotFound_ReturnsFailure));

        var service = new MeetingService(db);

        var result = await service.CreateAsync(
            new CreateMeetingRequestDto
            {
                Title = "X",
                Location = "Y",
                ScheduledAt = DateTimeOffset.UtcNow
            },
            Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetByInviteCodeAsync_WithValidCode_ReturnsMeeting()
    {
        await using var db = CreateDb(nameof(GetByInviteCodeAsync_WithValidCode_ReturnsMeeting));

        var host = new User { Id = Guid.NewGuid(), Email = "h@test.com", DisplayName = "H" };
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "My Meeting",
            Location = "Park",
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            InviteCode = "ABCD1234",
            HostId = host.Id,
            Host = host
        };
        db.Add(host);
        db.Add(meeting);
        await db.SaveChangesAsync();

        var service = new MeetingService(db);

        var result = await service.GetByInviteCodeAsync("ABCD1234");

        result.IsSuccess.Should().BeTrue();
        result.Value!.InviteCode.Should().Be("ABCD1234");
    }

    [Fact]
    public async Task GetByInviteCodeAsync_WithInvalidCode_ReturnsFailure()
    {
        await using var db = CreateDb(nameof(GetByInviteCodeAsync_WithInvalidCode_ReturnsFailure));

        var service = new MeetingService(db);

        var result = await service.GetByInviteCodeAsync("NOTEXIST");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_WhenNotHost_ReturnsFailure()
    {
        await using var db = CreateDb(nameof(CancelAsync_WhenNotHost_ReturnsFailure));

        var realHostId = Guid.NewGuid();
        var host = new User { Id = realHostId, Email = "h@test.com", DisplayName = "H" };
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            HostId = realHostId,
            Host = host,
            Title = "Test",
            Location = "Loc",
            ScheduledAt = DateTimeOffset.UtcNow,
            InviteCode = "AAAAAAAA"
        };
        db.Add(host);
        db.Add(meeting);
        await db.SaveChangesAsync();

        var service = new MeetingService(db);

        var result = await service.CancelAsync(meeting.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("host");
    }

    [Fact]
    public async Task CancelAsync_WhenAlreadyCompleted_ReturnsFailure()
    {
        await using var db = CreateDb(nameof(CancelAsync_WhenAlreadyCompleted_ReturnsFailure));

        var hostId = Guid.NewGuid();
        var host = new User { Id = hostId, Email = "h@test.com", DisplayName = "H" };
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            Host = host,
            Title = "Test",
            Location = "Loc",
            ScheduledAt = DateTimeOffset.UtcNow,
            InviteCode = "AAAAAAAA",
            Status = MeetingStatus.Completed
        };
        db.Add(host);
        db.Add(meeting);
        await db.SaveChangesAsync();

        var service = new MeetingService(db);

        var result = await service.CancelAsync(meeting.Id, hostId);

        result.IsSuccess.Should().BeFalse();
    }
}
