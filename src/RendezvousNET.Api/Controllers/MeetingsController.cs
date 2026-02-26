using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RendezvousNET.Api.Hubs;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Shared.DTOs.Meetings;
using System.Security.Claims;

namespace RendezvousNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MeetingsController : ControllerBase
{
    private readonly IMeetingService _meetingService;

    public MeetingsController(IMeetingService meetingService) =>
        _meetingService = meetingService;

    /// <summary>Create a new meeting. The authenticated user becomes the host.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MeetingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMeetingRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var hostId = GetCurrentUserId();
        if (hostId == Guid.Empty) return Unauthorized();

        var result = await _meetingService.CreateAsync(request, hostId, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>Get full meeting details by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MeetingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _meetingService.GetByIdAsync(id, cancellationToken);
        if (!result.IsSuccess) return NotFound(new { error = result.Error });

        return Ok(result.Value);
    }

    /// <summary>Resolve a meeting by its invite code (used by guests before joining).</summary>
    [HttpGet("by-code/{inviteCode}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MeetingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByInviteCode(string inviteCode, CancellationToken cancellationToken)
    {
        var result = await _meetingService.GetByInviteCodeAsync(inviteCode, cancellationToken);
        if (!result.IsSuccess) return NotFound(new { error = result.Error });

        return Ok(result.Value);
    }

    /// <summary>List all meetings hosted by the current user.</summary>
    [HttpGet("my")]
    [ProducesResponseType(typeof(IReadOnlyList<MeetingSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var hostId = GetCurrentUserId();
        if (hostId == Guid.Empty) return Unauthorized();

        var result = await _meetingService.GetByHostAsync(hostId, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>Cancel a meeting.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var result = await _meetingService.CancelAsync(id, userId, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
