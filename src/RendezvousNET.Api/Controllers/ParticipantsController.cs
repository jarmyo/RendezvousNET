using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RendezvousNET.Api.Hubs;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Shared.DTOs.Participants;
using System.Security.Claims;

namespace RendezvousNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ParticipantsController : ControllerBase
{
    private readonly IParticipantService _participantService;
    private readonly IHubContext<MeetingHub> _hubContext;

    public ParticipantsController(
        IParticipantService participantService,
        IHubContext<MeetingHub> hubContext)
    {
        _participantService = participantService;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Join a meeting via invite code.
    /// Anonymous guests can call this without authentication.
    /// </summary>
    [HttpPost("join")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MeetingParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Join(
        [FromBody] JoinMeetingRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        Guid? userId = null;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        if (Guid.TryParse(sub, out var id)) userId = id;

        var result = await _participantService.JoinMeetingAsync(request, userId, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    /// <summary>
    /// Update the real-time status of a participant.
    /// Broadcasts the change to all clients watching the meeting dashboard via SignalR.
    /// </summary>
    [HttpPatch("{participantId:guid}/status")]
    [ProducesResponseType(typeof(ParticipantStatusUpdatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid participantId,
        [FromBody] UpdateParticipantStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _participantService.UpdateStatusAsync(participantId, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.Contains("not found")
                ? NotFound(new { error = result.Error })
                : BadRequest(new { error = result.Error });
        }

        // Broadcast real-time status update to all dashboard clients for this meeting
        await MeetingHub.BroadcastStatusUpdateAsync(_hubContext, result.Value!, cancellationToken);

        return Ok(result.Value);
    }

    /// <summary>Get participant details by ID.</summary>
    [HttpGet("{participantId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(MeetingParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid participantId, CancellationToken cancellationToken)
    {
        var result = await _participantService.GetByIdAsync(participantId, cancellationToken);
        if (!result.IsSuccess) return NotFound(new { error = result.Error });

        return Ok(result.Value);
    }
}
