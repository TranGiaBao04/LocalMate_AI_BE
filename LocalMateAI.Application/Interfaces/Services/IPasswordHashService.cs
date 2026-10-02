using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPasswordHashService
{
    string HashPassword(User user, string password);

    PasswordHashVerificationResult VerifyHashedPassword(
        User user,
        string hashedPassword,
        string providedPassword);
}

public enum PasswordHashVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}
