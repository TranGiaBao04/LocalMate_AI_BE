using System.ComponentModel.DataAnnotations;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LocalMateAI.Infrastructure.Persistence;

public enum AdminSeedResult
{
    NotConfigured,
    InvalidConfiguration,
    AlreadyExists,
    Created
}

/// <summary>
/// BE-81: tạo tài khoản admin đầu tiên từ biến môi trường, chạy ở MỌI môi trường (kể cả Production).
/// Chỉ TẠO MỚI: email đã tồn tại thì không nâng quyền, không đổi mật khẩu. Cấu hình thiếu/sai chỉ ghi log,
/// không làm sập app. Không bao giờ ghi mật khẩu ra log.
/// </summary>
public static class AdminAccountSeeder
{
    private const int MaxEmailLength = 254;
    private const int MaxFullNameLength = 200;
    private const string UserEmailConstraintName = "UX_Users_Email";
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static async Task<AdminSeedResult> SeedAsync(
        AppDbContext context,
        IPasswordHashService passwordHashService,
        AdminSeedOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(passwordHashService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var email = options.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = options.Password ?? string.Empty;
        var fullName = string.IsNullOrWhiteSpace(options.FullName)
            ? AdminSeedOptions.DefaultFullName
            : options.FullName.Trim();

        if (email.Length == 0 && password.Length == 0)
        {
            logger.LogInformation("AdminSeed chưa cấu hình (AdminSeed__Email, AdminSeed__Password): bỏ qua tạo tài khoản admin.");
            return AdminSeedResult.NotConfigured;
        }

        var error = Validate(email, password, fullName);
        if (error is not null)
        {
            logger.LogError("AdminSeed cấu hình sai: {Reason} Bỏ qua tạo tài khoản admin.", error);
            return AdminSeedResult.InvalidConfiguration;
        }

        var existing = await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => new { RoleName = user.Role!.Name, user.Role.IsSystem })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            LogAlreadyExists(logger, email, existing.RoleName, existing.IsSystem);
            return AdminSeedResult.AlreadyExists;
        }

        var adminNormalizedName = SystemRoles.Normalize(SystemRoles.AdminName);
        var adminRoleId = await context.Roles
            .AsNoTracking()
            .Where(role => role.IsSystem && role.NormalizedName == adminNormalizedName)
            .Select(role => (Guid?)role.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy role hệ thống Admin để seed tài khoản admin (migration AddRbacAndUserStatus chưa chạy?).");

        var admin = new User { FullName = fullName, Email = email, RoleId = adminRoleId };
        admin.PasswordHash = passwordHashService.HashPassword(admin, password);
        context.Users.Add(admin);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception))
        {
            // Instance khác vừa tạo cùng email (nhiều instance khởi động cùng lúc).
            context.Entry(admin).State = EntityState.Detached;
            logger.LogInformation("Tài khoản admin {Email} vừa được tạo bởi tiến trình khác.", email);
            return AdminSeedResult.AlreadyExists;
        }

        // Email đã thành tài khoản thật: bản đăng ký bằng OTP đang chờ (nếu có) không còn cần.
        await context.PendingRegistrations
            .Where(pending => pending.Email == email)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Đã tạo tài khoản admin {Email} từ AdminSeed.", email);
        return AdminSeedResult.Created;
    }

    private static string? Validate(string email, string password, string fullName)
    {
        if (email.Length == 0)
        {
            return "Thiếu AdminSeed__Email.";
        }

        if (email.Length > MaxEmailLength || !EmailValidator.IsValid(email))
        {
            return "AdminSeed__Email không đúng định dạng email.";
        }

        if (password.Length == 0)
        {
            return "Thiếu AdminSeed__Password.";
        }

        // Chỉ báo mật khẩu sai luật, không in giá trị mật khẩu.
        if (PasswordRules.GetError(password) is { } passwordError)
        {
            return $"AdminSeed__Password không hợp lệ ({passwordError})";
        }

        return fullName.Length > MaxFullNameLength
            ? $"AdminSeed__FullName tối đa {MaxFullNameLength} ký tự."
            : null;
    }

    private static void LogAlreadyExists(ILogger logger, string email, string roleName, bool isSystemRole)
    {
        var isAdmin = isSystemRole && roleName == SystemRoles.AdminName;
        if (isAdmin)
        {
            logger.LogInformation("Tài khoản admin {Email} đã tồn tại: không thay đổi.", email);
            return;
        }

        logger.LogWarning(
            "Email AdminSeed {Email} đã là tài khoản role {Role}: KHÔNG nâng quyền, không đổi mật khẩu. "
            + "Dùng email khác hoặc gán role Admin qua API quản lý role.",
            email,
            roleName);
    }

    private static bool IsDuplicateEmail(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserEmailConstraintName
        };
}
