namespace LocalMateAI.Application.Interfaces.Services;

public interface IEmailSender
{
    // Trả false nếu gửi thất bại (lỗi đã được ghi log bên trong), không ném lỗi ra ngoài.
    Task<bool> TrySendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
