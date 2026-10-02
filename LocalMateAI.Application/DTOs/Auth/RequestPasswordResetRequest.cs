using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed class RequestPasswordResetRequest
{
    [Required]
    public string? Email { get; init; }
}
