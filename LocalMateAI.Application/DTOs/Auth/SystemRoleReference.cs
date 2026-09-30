namespace LocalMateAI.Application.DTOs.Auth;

/// <summary>BE-82: Id + tên của một role hệ thống (User/Admin), tra theo tên từ DB.</summary>
public sealed record SystemRoleReference(Guid Id, string Name);
