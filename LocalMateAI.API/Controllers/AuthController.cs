using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);

        return result.Status switch
        {
            RegisterResultStatus.Success =>
                StatusCode(StatusCodes.Status201Created, result.Response),
            RegisterResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            RegisterResultStatus.DuplicateEmail =>
                Conflict(CreateDuplicateEmailProblem()),
            _ => throw new InvalidOperationException("Unsupported register result status.")
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
