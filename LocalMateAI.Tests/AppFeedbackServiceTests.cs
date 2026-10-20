using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.AppFeedback;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class AppFeedbackServiceTests
{
    private readonly FakeAppFeedbackRepository _repository = new();
    private readonly CreateAppFeedbackRequestValidator _validator = new();
    private readonly FixedTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.FromHours(7)));

    private AppFeedbackService CreateService() => new(_repository, _validator, _timeProvider);

    [Theory]
    [InlineData("/draft", "/draft")]
    [InlineData("/profile", "/profile")]
    [InlineData("/place/123", "/place/123")]
    public void SanitizePagePath_ValidPaths_PreservesPath(string input, string expected)
    {
        var result = AppFeedbackService.SanitizePagePath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/draft?token=secret")]
    [InlineData("/draft#step2")]
    [InlineData("profile")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SanitizePagePath_InvalidOrSensitivePaths_ReturnsNull(string? input)
    {
        var result = AppFeedbackService.SanitizePagePath(input);
        Assert.Null(result);
    }

    [Fact]
    public void SanitizePagePath_Exceeding200Chars_ReturnsNull()
    {
        var longPath = "/" + new string('a', 201);
        var result = AppFeedbackService.SanitizePagePath(longPath);
        Assert.Null(result);
    }

    [Fact]
    public void SanitizeUserAgent_TruncatesLongStringAt500Chars()
    {
        var longUserAgent = new string('x', 600);
        var result = AppFeedbackService.SanitizeUserAgent(longUserAgent);

        Assert.NotNull(result);
        Assert.Equal(500, result.Length);
        Assert.Equal(new string('x', 500), result);
    }

    [Fact]
    public async Task SubmitFeedback_WhenUnderDailyLimit_Succeeds()
    {
        var service = CreateService();
        _repository.CurrentCount = 4; // 4 prior submissions today

        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Bug,
            4,
            "Chặn thứ 2 bị lỗi màn hình trắng.",
            "/draft");

        var result = await service.SubmitFeedbackAsync(Guid.NewGuid(), request, "Mozilla/5.0");

        Assert.Equal(CreateAppFeedbackResultStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.NotNull(_repository.LastCreated);
        Assert.Equal("Chặn thứ 2 bị lỗi màn hình trắng.", _repository.LastCreated.Content);
        Assert.Equal("/draft", _repository.LastCreated.PagePath);
    }

    [Fact]
    public async Task SubmitFeedback_WhenLimitReached_ReturnsDailyLimitReachedWithResetAt()
    {
        var service = CreateService();
        _repository.CurrentCount = 5; // Already 5 today

        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Suggestion,
            5,
            "Nên cho chọn nhiều điểm hơn.",
            "/profile");

        var result = await service.SubmitFeedbackAsync(Guid.NewGuid(), request, "Mozilla/5.0");

        Assert.Equal(CreateAppFeedbackResultStatus.DailyLimitReached, result.Status);
        Assert.NotNull(result.ResetAt);
        Assert.Null(_repository.LastCreated);
    }

    [Fact]
    public async Task SubmitFeedback_InvalidBody_ReturnsInvalidInput()
    {
        var service = CreateService();
        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Bug,
            10, // Invalid rating
            "", // Empty content
            "/draft");

        var result = await service.SubmitFeedbackAsync(Guid.NewGuid(), request, "Mozilla/5.0");

        Assert.Equal(CreateAppFeedbackResultStatus.InvalidInput, result.Status);
        Assert.NotNull(result.ValidationErrors);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeAppFeedbackRepository : IAppFeedbackRepository
    {
        public int CurrentCount { get; set; }
        public AppFeedback? LastCreated { get; private set; }

        public Task<int> CountUserSubmissionsSinceUtcAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CurrentCount);
        }

        public Task<AppFeedback> CreateAsync(AppFeedback feedback, CancellationToken cancellationToken = default)
        {
            feedback.Id = Guid.NewGuid();
            feedback.CreatedAt = DateTime.UtcNow;
            LastCreated = feedback;
            return Task.FromResult(feedback);
        }

        public Task<PagedResult<AdminAppFeedbackItemResponse>> GetAdminListAsync(
            AdminAppFeedbackQuery query,
            DateTime? fromUtc,
            DateTime? toUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PagedResult<AdminAppFeedbackItemResponse>([], 1, 20, 0, 0));
        }
    }
}
