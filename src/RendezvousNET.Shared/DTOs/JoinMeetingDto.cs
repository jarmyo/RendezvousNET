using System.ComponentModel.DataAnnotations;

namespace RendezvousNET.Shared.DTOs;

public class JoinMeetingDto
{
    [Required]
    [MaxLength(20)]
    public string InviteCode { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    public string? TrustedFriendName { get; set; }
    public string? TrustedFriendEmail { get; set; }
    public string? TrustedFriendPhone { get; set; }
}
