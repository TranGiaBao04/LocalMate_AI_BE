namespace LocalMateAI.Application.DTOs.Users;

public sealed record UserProfileResponse(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    DateTime CreatedAt,
    UserPreferencesResponse Preferences,
    // BE-82: quyền thực tế (Admin = mọi quyền), để FE ẩn/hiện menu admin.
    IReadOnlyList<string> Permissions);
