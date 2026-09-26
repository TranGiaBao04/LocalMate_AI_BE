using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed class VerifyRegistrationRequest
{
    [Required]
    public string? Email { get; init; }

    [Required]
    public string? Code { get; init; }
}
