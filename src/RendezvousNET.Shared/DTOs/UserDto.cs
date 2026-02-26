namespace RendezvousNET.Shared.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
