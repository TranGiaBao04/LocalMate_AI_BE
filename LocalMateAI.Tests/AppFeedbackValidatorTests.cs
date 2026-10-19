using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.Validators.AppFeedback;
using LocalMateAI.Domain.Enums;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class AppFeedbackValidatorTests
{
    private readonly CreateAppFeedbackRequestValidator _createValidator = new();
    private readonly AdminAppFeedbackQueryValidator _adminValidator = new();

    [Fact]
    public void CreateRequest_ValidPayload_PassesValidation()
    {
        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Bug,
            4,
            "Bấm Thay địa điểm ở chặng 2 thì màn hình trắng.",
            "/draft");

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void CreateRequest_RatingOutOfRange_FailsValidation(int rating)
    {
        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Suggestion,
            rating,
            "Nội dung góp ý hợp lệ.",
            "/home");

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAppFeedbackRequest.Rating));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateRequest_EmptyContent_FailsValidation(string? content)
    {
        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Other,
            null,
            content!,
            "/profile");

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAppFeedbackRequest.Content));
    }

    [Fact]
    public void CreateRequest_ContentExceeding1000Chars_FailsValidation()
    {
        var longContent = new string('a', 1001);
        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Bug,
            3,
            longContent,
            "/draft");

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAppFeedbackRequest.Content));
    }

    [Fact]
    public void CreateRequest_ContentExactly1000Chars_PassesValidation()
    {
        var exactContent = new string('a', 1000);
        var request = new CreateAppFeedbackRequest(
            AppFeedbackType.Praise,
            5,
            exactContent,
            null);

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AdminQuery_ValidDefault_PassesValidation()
    {
        var query = new AdminAppFeedbackQuery();
        var result = _adminValidator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AdminQuery_ToBeforeFrom_FailsValidation()
    {
        var query = new AdminAppFeedbackQuery
        {
            From = "2026-10-20",
            To = "2026-10-19"
        };

        var result = _adminValidator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminAppFeedbackQuery.To));
    }

    [Theory]
    [InlineData("invalid-date")]
    [InlineData("2026/10/20")]
    public void AdminQuery_InvalidDateFormat_FailsValidation(string invalidDate)
    {
        var query = new AdminAppFeedbackQuery { From = invalidDate };
        var result = _adminValidator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminAppFeedbackQuery.From));
    }
}
