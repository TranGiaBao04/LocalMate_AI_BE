using FluentValidation;
using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

public sealed class AppFeedbackService(
    IAppFeedbackRepository repository,
    IValidator<CreateAppFeedbackRequest> requestValidator,
    TimeProvider timeProvider) : IAppFeedbackService
{
    public const int DailySubmissionLimit = 5;
    public const int MaxPagePathLength = 200;
    public const int MaxUserAgentLength = 500;

    public async Task<CreateAppFeedbackResult> SubmitFeedbackAsync(
        Guid userId,
        CreateAppFeedbackRequest request,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = await requestValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return new CreateAppFeedbackResult(
                CreateAppFeedbackResultStatus.InvalidInput,
                ValidationErrors: validationResult.ToDictionary());
        }

        var startOfTodayUtc = VietnamTime.StartOfTodayUtc(timeProvider);
        var todayCount = await repository.CountUserSubmissionsSinceUtcAsync(userId, startOfTodayUtc, cancellationToken);
        if (todayCount >= DailySubmissionLimit)
        {
            var resetAt = startOfTodayUtc.AddDays(1);
            return new CreateAppFeedbackResult(
                CreateAppFeedbackResultStatus.DailyLimitReached,
                ResetAt: resetAt);
        }

        var sanitizedPagePath = SanitizePagePath(request.PagePath);
        var sanitizedUserAgent = SanitizeUserAgent(userAgent);
        var content = request.Content.Trim();

        var feedback = new AppFeedback
        {
            UserId = userId,
            Type = request.Type,
            Rating = request.Rating,
            Content = content,
            PagePath = sanitizedPagePath,
            UserAgent = sanitizedUserAgent,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            UpdatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        var created = await repository.CreateAsync(feedback, cancellationToken);
        return new CreateAppFeedbackResult(
            CreateAppFeedbackResultStatus.Success,
            Response: new AppFeedbackCreatedResponse(created.Id, created.CreatedAt));
    }

    public Task<PagedResult<AdminAppFeedbackItemResponse>> GetAdminListAsync(
        AdminAppFeedbackQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        DateTime? fromUtc = DashboardDateRules.TryParseDate(query.From, out var fromDate)
            ? DashboardDateRules.StartOfDayUtc(fromDate)
            : null;

        DateTime? toUtc = DashboardDateRules.TryParseDate(query.To, out var toDate)
            ? DashboardDateRules.StartOfDayUtc(toDate.AddDays(1))
            : null;

        return repository.GetAdminListAsync(query, fromUtc, toUtc, cancellationToken);
    }

    public static string? SanitizePagePath(string? pagePath)
    {
        if (string.IsNullOrWhiteSpace(pagePath))
        {
            return null;
        }

        var trimmed = pagePath.Trim();
        if (!trimmed.StartsWith('/') || trimmed.Length > MaxPagePathLength || trimmed.Contains('?') || trimmed.Contains('#'))
        {
            return null;
        }

        return trimmed;
    }

    public static string? SanitizeUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var trimmed = userAgent.Trim();
        return trimmed.Length <= MaxUserAgentLength ? trimmed : trimmed[..MaxUserAgentLength];
    }
}
