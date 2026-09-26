using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed class ConfirmPasswordResetRequest
{
    [Required]
    public string? Email { get; init; }

    [Required]
    public string? Code { get; init; }

    [Required]
    public string? NewPassword { get; init; }
}
