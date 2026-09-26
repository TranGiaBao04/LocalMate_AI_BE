using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IEmailOtpService
{
    Task<OtpIssueResult> IssueAsync(
        string email,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<OtpVerifyResult> VerifyAsync(
        string email,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken = default);
}
