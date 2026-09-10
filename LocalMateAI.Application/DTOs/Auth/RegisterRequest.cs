using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required]
    public string? FullName { get; init; }

    [Required]
    public string? Email { get; init; }

    [Required]
    public string? Password { get; init; }
}
