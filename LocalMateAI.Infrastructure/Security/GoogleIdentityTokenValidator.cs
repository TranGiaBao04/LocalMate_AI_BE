using Google.Apis.Auth;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Security;

public sealed class GoogleIdentityTokenValidator(
    IOptions<GoogleAuthOptions> googleAuthOptions) : IGoogleIdentityTokenValidator
{
    public async Task<VerifiedExternalIdentity?> ValidateAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [googleAuthOptions.Value.ClientId]
                });

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(payload.Subject)
                || string.IsNullOrWhiteSpace(payload.Email)
                || !payload.EmailVerified)
            {
                return null;
            }

            var email = payload.Email.Trim();
            var fullName = string.IsNullOrWhiteSpace(payload.Name)
                ? email
                : payload.Name.Trim();

            return new VerifiedExternalIdentity(
                payload.Subject.Trim(),
                email,
                fullName);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
