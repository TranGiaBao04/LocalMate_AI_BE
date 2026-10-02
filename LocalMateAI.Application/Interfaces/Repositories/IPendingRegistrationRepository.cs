using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPendingRegistrationRepository
{
    Task<PendingRegistration?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    // Tạo mới hoặc ghi đè (theo Email) bản đăng ký đang chờ OTP.
    Task UpsertAsync(
        string email,
        string fullName,
        string passwordHash,
        DateTime expiresAt,
        DateTime now,
        CancellationToken cancellationToken = default);

    // Tạo User thật và xoá bản đăng ký tạm trong cùng transaction. False nếu email đã có tài khoản.
    Task<bool> TryCompleteRegistrationAsync(
        User user,
        CancellationToken cancellationToken = default);

    Task DeleteByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);
}
