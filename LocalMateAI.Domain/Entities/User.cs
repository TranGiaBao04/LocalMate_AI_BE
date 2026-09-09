using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class User
{
    public long Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
