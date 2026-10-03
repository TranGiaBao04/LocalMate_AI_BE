using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Validators.Users;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminUserQueryValidatorTests
{
    private readonly AdminUserQueryValidator validator = new();

    [Fact]
    public void DefaultQuery_IsValid()
    {
        Assert.True(validator.Validate(new AdminUserQuery()).IsValid);
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("FULLNAME")]
    [InlineData("email")]
    [InlineData("status")]
    public void AllowedSortFields_AreValid(string sortBy)
    {
        Assert.True(validator.Validate(new AdminUserQuery { SortBy = sortBy }).IsValid);
    }

    [Fact]
    public void UnknownSortField_FailsOnSortBy()
    {
        var result = validator.Validate(new AdminUserQuery { SortBy = "role" });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminUserQuery.SortBy));
    }

    [Fact]
    public void UndefinedStatus_FailsOnStatus()
    {
        var result = validator.Validate(new AdminUserQuery { Status = (UserStatus)9 });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminUserQuery.Status));
    }

    [Fact]
    public void PlanLongerThanLimit_FailsOnPlan()
    {
        var tooLong = new string('A', AdminUserQuery.MaxPlanLength + 1);

        var result = validator.Validate(new AdminUserQuery { Plan = tooLong });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminUserQuery.Plan));
        Assert.True(validator.Validate(new AdminUserQuery { Plan = $"  {tooLong[1..]}  " }).IsValid);
    }
}

public sealed class AdminUserTripQueryValidatorTests
{
    private readonly AdminUserTripQueryValidator validator = new();

    [Theory]
    [InlineData("createdAt")]
    [InlineData("plannedStartAt")]
    [InlineData("FINALIZEDAT")]
    public void AllowedSortFields_AreValid(string sortBy)
    {
        Assert.True(validator.Validate(new AdminUserTripQuery { SortBy = sortBy }).IsValid);
    }

    [Fact]
    public void UnknownSortFieldAndStatus_FailOnTheirFields()
    {
        var result = validator.Validate(new AdminUserTripQuery { SortBy = "budgetMax", Status = (TripStatus)7 });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminUserTripQuery.SortBy));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminUserTripQuery.Status));
    }
}
