namespace RendezvousNET.Shared.DTOs;

public class TrustedFriendDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsNotified { get; set; }
}
