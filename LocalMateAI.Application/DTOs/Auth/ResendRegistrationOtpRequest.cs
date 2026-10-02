using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed class ResendRegistrationOtpRequest
{
    [Required]
    public string? Email { get; init; }
}
