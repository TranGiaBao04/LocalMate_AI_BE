using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Security;

public sealed class AspNetCorePasswordHashService(
    IOptions<PasswordHasherOptions> options) : IPasswordHashService
{
    private readonly PasswordHasher<User> passwordHasher = new(options);

    public string HashPassword(User user, string password) =>
        passwordHasher.HashPassword(user, password);

    public PasswordHashVerificationResult VerifyHashedPassword(
        User user,
        string hashedPassword,
        string providedPassword) =>
        passwordHasher.VerifyHashedPassword(user, hashedPassword, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordHashVerificationResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded =>
                PasswordHashVerificationResult.SuccessRehashNeeded,
            _ => PasswordHashVerificationResult.Failed
        };
}
