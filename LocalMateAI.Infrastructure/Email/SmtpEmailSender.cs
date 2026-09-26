using LocalMateAI.Application.Interfaces.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LocalMateAI.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> smtpOptions,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private const int TimeoutMilliseconds = 15_000;
    private const int ImplicitTlsPort = 465;

    public async Task<bool> TrySendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = smtpOptions.Value;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(options.FromName, options.Username));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            var socketOptions = options.Port == ImplicitTlsPort
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
            await client.ConnectAsync(options.Host, options.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Không ghi địa chỉ nhận hay nội dung mail vào log để tránh lộ mã OTP/thông tin cá nhân.
            logger.LogError(exception, "Failed to send email via SMTP.");
            return false;
        }
    }
}
