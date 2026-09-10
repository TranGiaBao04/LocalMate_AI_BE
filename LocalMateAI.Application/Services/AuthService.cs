using System.ComponentModel.DataAnnotations;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces;
using LocalMateAI.Application.Persistence;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Application.Services;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHashService passwordHashService) : IAuthService
{
    private const string UserEmailConstraintName = "UX_USERS_Email";
    private const int MaximumFullNameLength = 200;
    private const int MaximumEmailLength = 254;
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;

    private static readonly EmailAddressAttribute EmailValidator = new();

    public async Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var validationErrors = Validate(fullName, email, password);
        if (validationErrors.Count > 0)
        {
            return RegisterResult.ValidationFailed(validationErrors);
        }

        var emailExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Email == email, cancellationToken);

        if (emailExists)
        {
            return RegisterResult.EmailAlreadyExists();
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            Role = UserRole.User
        };

        user.PasswordHash = passwordHashService.HashPassword(user, password);
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return RegisterResult.EmailAlreadyExists();
        }

        var response = new RegisterResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.Role.ToString(),
            user.CreatedAt);

        return RegisterResult.Succeeded(response);
    }

    private static Dictionary<string, string[]> Validate(
        string fullName,
        string email,
        string password)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["fullName"] = ["Full name is required."];
        }
        else if (fullName.Length > MaximumFullNameLength)
        {
            errors["fullName"] = [$"Full name must not exceed {MaximumFullNameLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > MaximumEmailLength)
        {
            errors["email"] = [$"Email must not exceed {MaximumEmailLength} characters."];
        }
        else if (!EmailValidator.IsValid(email))
        {
            errors["email"] = ["Email format is invalid."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length < MinimumPasswordLength)
        {
            errors["password"] = [$"Password must contain at least {MinimumPasswordLength} characters."];
        }
        else if (password.Length > MaximumPasswordLength)
        {
            errors["password"] = [$"Password must not exceed {MaximumPasswordLength} characters."];
        }
        else if (string.IsNullOrWhiteSpace(password))
        {
            errors["password"] = ["Password must contain at least one non-whitespace character."];
        }

        return errors;
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            postgresException.ConstraintName,
            UserEmailConstraintName,
            StringComparison.Ordinal);
}
