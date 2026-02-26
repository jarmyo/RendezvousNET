using System.ComponentModel.DataAnnotations;

namespace RendezvousNET.Shared.DTOs;

public class CreateMeetingDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(500)]
    public string Location { get; set; } = string.Empty;

    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }

    [Required]
    public DateTimeOffset ScheduledAt { get; set; }
}
