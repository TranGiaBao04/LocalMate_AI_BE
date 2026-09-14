using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IGoogleIdentityTokenValidator
{
    Task<VerifiedExternalIdentity?> ValidateAsync(
        string idToken,
        CancellationToken cancellationToken = default);
}
