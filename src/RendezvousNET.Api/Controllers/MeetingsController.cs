using Microsoft.AspNetCore.Mvc;
using RendezvousNET.Application.Services;
using RendezvousNET.Domain.Interfaces;
using RendezvousNET.Shared.DTOs;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MeetingsController(MeetingService meetingService, IMeetingRepository meetingRepo) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MeetingDto>> Create(
        [FromBody] CreateMeetingDto dto,
        CancellationToken ct)
    {
        // TODO: Replace with real user ID extracted from validated JWT claims (requires auth middleware)
        var hostId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var result = await meetingService.CreateMeetingAsync(hostId, dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MeetingDto>> GetById(Guid id, CancellationToken ct)
    {
        var meeting = await meetingRepo.GetByIdAsync(id, ct);
        if (meeting is null) return NotFound();

        return Ok(new MeetingDto
        {
            Id = meeting.Id,
            Title = meeting.Title,
            Description = meeting.Description,
            Location = meeting.Location,
            LocationLatitude = meeting.LocationLatitude,
            LocationLongitude = meeting.LocationLongitude,
            ScheduledAt = meeting.ScheduledAt,
            Status = meeting.Status,
            HostName = meeting.Host?.DisplayName ?? string.Empty,
            InviteCode = meeting.InviteCode,
            Participants = meeting.Participants?.Select(p => new MeetingParticipantDto
            {
                Id = p.Id,
                MeetingId = p.MeetingId,
                DisplayName = p.DisplayName,
                IsAnonymous = p.IsAnonymous,
                Status = p.Status,
                CurrentLatitude = p.CurrentLatitude,
                CurrentLongitude = p.CurrentLongitude,
                CheckInAt = p.CheckInAt
            }).ToList() ?? []
        });
    }

    [HttpPost("join")]
    public async Task<ActionResult<MeetingParticipantDto>> Join(
        [FromBody] JoinMeetingDto dto,
        CancellationToken ct)
    {
        // TODO: Get userId from JWT if authenticated, null for anonymous guests
        Guid? userId = null;
        var result = await meetingService.JoinMeetingAsync(userId, dto, ct);
        return Ok(result);
    }

    [HttpPut("participants/status")]
    public async Task<IActionResult> UpdateStatus(
        [FromBody] UpdateParticipantStatusDto dto,
        CancellationToken ct)
    {
        await meetingService.UpdateParticipantStatusAsync(dto, ct);
        return NoContent();
    }
}
