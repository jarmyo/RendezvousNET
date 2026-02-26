using System.ComponentModel.DataAnnotations;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs;

public class UpdateParticipantStatusDto
{
    [Required]
    public Guid ParticipantId { get; set; }

    [Required]
    public ParticipantStatus Status { get; set; }

    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
}
