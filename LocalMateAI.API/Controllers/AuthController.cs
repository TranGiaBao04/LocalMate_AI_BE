using System.Globalization;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(OtpRateLimitPolicy.Name)]
    [ProducesResponseType<OtpDispatchResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpDispatchResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);

        return result.Status switch
        {
            RegisterResultStatus.Success =>
                Accepted(result.Response),
            RegisterResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            RegisterResultStatus.DuplicateEmail =>
                Conflict(CreateDuplicateEmailProblem()),
            RegisterResultStatus.Cooldown =>
                CreateOtpCooldownResult(result.RetryAfterSeconds),
            RegisterResultStatus.RateLimited =>
                CreateOtpRateLimitedResult(result.RetryAfterSeconds),
            _ => throw new InvalidOperationException("Unsupported register result status.")
        };
    }

    [HttpPost("register/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(OtpRateLimitPolicy.Name)]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegisterResponse>> VerifyRegistrationAsync(
        [FromBody] VerifyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyRegistrationAsync(request, cancellationToken);

        return result.Status switch
        {
            VerifyRegistrationResultStatus.Success =>
                StatusCode(StatusCodes.Status201Created, result.Response),
            VerifyRegistrationResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            VerifyRegistrationResultStatus.InvalidOtp =>
                BadRequest(CreateInvalidOtpProblem(result.RemainingAttempts)),
            VerifyRegistrationResultStatus.OtpExpired =>
                BadRequest(CreateOtpExpiredProblem()),
            VerifyRegistrationResultStatus.OtpAttemptsExceeded =>
                BadRequest(CreateOtpAttemptsExceededProblem()),
            VerifyRegistrationResultStatus.DuplicateEmail =>
                Conflict(CreateDuplicateEmailProblem()),
            _ => throw new InvalidOperationException("Unsupported registration verify result status.")
        };
    }

    [HttpPost("register/resend")]
    [AllowAnonymous]
    [EnableRateLimiting(OtpRateLimitPolicy.Name)]
    [ProducesResponseType<OtpDispatchResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpDispatchResponse>> ResendRegistrationOtpAsync(
        [FromBody] ResendRegistrationOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.ResendRegistrationOtpAsync(request, cancellationToken);

        return result.Status switch
        {
            OtpRequestResultStatus.Accepted =>
                Accepted(result.Response),
            OtpRequestResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            OtpRequestResultStatus.Cooldown =>
                CreateOtpCooldownResult(result.RetryAfterSeconds),
            OtpRequestResultStatus.RateLimited =>
                CreateOtpRateLimitedResult(result.RetryAfterSeconds),
            _ => throw new InvalidOperationException("Unsupported OTP request result status.")
        };
    }

    [HttpPost("password-reset/request")]
    [AllowAnonymous]
    [EnableRateLimiting(OtpRateLimitPolicy.Name)]
    [ProducesResponseType<OtpDispatchResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpDispatchResponse>> RequestPasswordResetAsync(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RequestPasswordResetAsync(request, cancellationToken);

        return result.Status switch
        {
            OtpRequestResultStatus.Accepted =>
                Accepted(result.Response),
            OtpRequestResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            OtpRequestResultStatus.GoogleAccountWithoutPassword =>
                Conflict(CreatePasswordResetGoogleAccountProblem()),
            OtpRequestResultStatus.Cooldown =>
                CreateOtpCooldownResult(result.RetryAfterSeconds),
            OtpRequestResultStatus.RateLimited =>
                CreateOtpRateLimitedResult(result.RetryAfterSeconds),
            _ => throw new InvalidOperationException("Unsupported password reset request result status.")
        };
    }

    [HttpPost("password-reset/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting(OtpRateLimitPolicy.Name)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmPasswordResetAsync(
        [FromBody] ConfirmPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.ConfirmPasswordResetAsync(request, cancellationToken);

        return result.Status switch
        {
            PasswordResetResultStatus.Success =>
                NoContent(),
            PasswordResetResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            PasswordResetResultStatus.InvalidOtp =>
                BadRequest(CreateInvalidOtpProblem(result.RemainingAttempts)),
            PasswordResetResultStatus.OtpExpired =>
                BadRequest(CreateOtpExpiredProblem()),
            PasswordResetResultStatus.OtpAttemptsExceeded =>
                BadRequest(CreateOtpAttemptsExceededProblem()),
            _ => throw new InvalidOperationException("Unsupported password reset confirm result status.")
        };
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);

        return result.Status switch
        {
            LoginResultStatus.Success => Ok(result.Response),
            LoginResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            LoginResultStatus.InvalidCredentials =>
                Unauthorized(CreateInvalidCredentialsProblem()),
            _ => throw new InvalidOperationException("Unsupported login result status.")
        };
    }

    [HttpPost("google")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoginResponse>> GoogleSignInAsync(
        [FromBody] GoogleSignInRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.GoogleSignInAsync(request, cancellationToken);

        return result.Status switch
        {
            GoogleSignInResultStatus.Success => Ok(result.Response),
            GoogleSignInResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            GoogleSignInResultStatus.InvalidGoogleToken =>
                Unauthorized(CreateInvalidGoogleTokenProblem()),
            GoogleSignInResultStatus.AccountLinkRequired =>
                Conflict(CreateAccountLinkRequiredProblem()),
            GoogleSignInResultStatus.AccountConflict =>
                Conflict(CreateAccountConflictProblem()),
            _ => throw new InvalidOperationException("Unsupported Google sign-in result status.")
        };
    }

    [HttpPost("demo")]
    [AllowAnonymous]
    [ProducesResponseType<DemoSessionResponse>(StatusCodes.Status200OK)]
    public ActionResult<DemoSessionResponse> CreateDemoSession()
    {
        var response = authService.CreateDemoSession();
        return Ok(response);
    }

    private ObjectResult CreateValidationProblem(
        IReadOnlyDictionary<string, string[]> validationErrors)
    {
        var problem = new ValidationProblemDetails(
            validationErrors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private ProblemDetails CreateDuplicateEmailProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Email is already registered.",
            Type = "https://httpstatuses.com/409",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "duplicate_email";
        return problem;
    }

    private ProblemDetails CreateInvalidOtpProblem(int? remainingAttempts)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid verification code.",
            Detail = "Mã xác thực không đúng.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_otp";
        if (remainingAttempts is not null)
        {
            problem.Extensions["remainingAttempts"] = remainingAttempts;
        }

        return problem;
    }

    private ProblemDetails CreateOtpExpiredProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Verification code has expired.",
            Detail = "Mã xác thực đã hết hạn, vui lòng yêu cầu mã mới.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "otp_expired";
        return problem;
    }

    private ProblemDetails CreateOtpAttemptsExceededProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Too many incorrect attempts.",
            Detail = "Bạn đã nhập sai quá nhiều lần, vui lòng yêu cầu mã mới.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "otp_attempts_exceeded";
        return problem;
    }

    private ObjectResult CreateOtpCooldownResult(int retryAfterSeconds) =>
        CreateTooManyRequestsResult(
            "otp_resend_cooldown",
            "Verification code was sent recently.",
            "Mã vừa được gửi, vui lòng đợi trước khi yêu cầu mã mới.",
            retryAfterSeconds);

    private ObjectResult CreateOtpRateLimitedResult(int retryAfterSeconds) =>
        CreateTooManyRequestsResult(
            "otp_rate_limited",
            "Too many verification codes requested.",
            "Bạn đã yêu cầu quá nhiều mã, vui lòng thử lại sau.",
            retryAfterSeconds);

    private ObjectResult CreateTooManyRequestsResult(
        string code,
        string title,
        string detail,
        int retryAfterSeconds)
    {
        Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = title,
            Detail = detail,
            Type = "https://httpstatuses.com/429",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = code;
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    private ProblemDetails CreatePasswordResetGoogleAccountProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Password reset is not available for Google accounts.",
            Detail = "Tài khoản này đăng nhập bằng Google, vui lòng dùng nút Đăng nhập bằng Google.",
            Type = "https://httpstatuses.com/409",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "password_reset_google_account";
        return problem;
    }

    private ProblemDetails CreateInvalidCredentialsProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Authentication failed.",
            Detail = "Email hoặc mật khẩu không chính xác.",
            Type = "https://httpstatuses.com/401",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_credentials";
        return problem;
    }

    private ProblemDetails CreateInvalidGoogleTokenProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Google authentication failed.",
            Detail = "The Google ID token is invalid.",
            Type = "https://httpstatuses.com/401",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_google_token";
        return problem;
    }

    private ProblemDetails CreateAccountLinkRequiredProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Account linking is required.",
            Detail = "Sign in with the existing LocalMate account before linking Google.",
            Type = "https://httpstatuses.com/409",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "account_link_required";
        return problem;
    }

    private ProblemDetails CreateAccountConflictProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "External identity conflict.",
            Detail = "The Google identity could not be associated with a LocalMate account.",
            Type = "https://httpstatuses.com/409",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "account_conflict";
        return problem;
    }
}
