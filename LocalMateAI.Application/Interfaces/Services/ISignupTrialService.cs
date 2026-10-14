namespace LocalMateAI.Application.Interfaces.Services;

public enum SignupTrialOutcome
{
    Granted,
    NoProgram,
    Full,
    PlanUnavailable,
    AlreadyGranted
}

public interface ISignupTrialService
{
    // Cấp gói dùng thử cho tài khoản vừa tạo nếu đang có chương trình chạy. Lỗi hạ tầng thì ném exception.
    Task<SignupTrialOutcome> GrantAsync(Guid userId, CancellationToken cancellationToken = default);
}
