using System.ComponentModel.DataAnnotations;

namespace LocalMateAI.Application.DTOs.Auth;

public sealed record GoogleSignInRequest(
    [Required]
    [StringLength(16_384)]
    string? IdToken);
