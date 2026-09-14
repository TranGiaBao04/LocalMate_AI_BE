using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public ICollection<UserPreferenceTag> PreferenceTags { get; set; } = [];
}
