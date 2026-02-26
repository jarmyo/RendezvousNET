using Microsoft.AspNetCore.Mvc;
using RendezvousNET.Application.Services;
using RendezvousNET.Shared.DTOs;

namespace RendezvousNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MeetingsController(MeetingService meetingService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MeetingDto>> Create(
        [FromBody] CreateMeetingDto dto,
        CancellationToken ct)
    {
        // TODO: Replace with real user ID from JWT claims
        var hostId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var result = await meetingService.CreateMeetingAsync(hostId, dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MeetingDto>> GetById(Guid id, CancellationToken ct)
    {
        // TODO: fetch from repository and return
        return NotFound();
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
