using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed record LoginRequest(
    [Required] string? Email,
    [Required] string? Password);
