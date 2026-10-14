using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.AdminAiUsage;
using LocalMateAI.Application.Validators.Dashboard;

namespace LocalMateAI.Tests;

public sealed class AdminAiUsageQueryValidatorTests
{
    [Fact]
    public void V02_ExplicitMinimumDateRange_DoesNotEvaluateAnUnusedDefault()
    {
        var query = new DashboardDateRangeQuery { From = "0001-01-01", To = "0001-01-01" };
        Assert.True(new DashboardDateRangeQueryValidator(new FixedClock()).Validate(query).IsValid);
        var range = DashboardDateRules.Resolve(query, new DateOnly(2026, 10, 13));
        Assert.Equal(DateOnly.MinValue, range!.From);
        Assert.Equal(DateOnly.MinValue, range.To);
    }

    [Theory]
    [InlineData(0, 20, "Page")]
    [InlineData(10001, 20, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public void C02_V01_PagingErrorsHaveCorrectFields(int page, int size, string field)
    {
        var result = Validate(new() { Page = page, PageSize = size });
        Assert.Equal(field, Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("id", "desc", "SortBy")]
    [InlineData("startedAt", "up", "SortDirection")]
    public void C03_V01_SortWhitelist(string field, string direction, string error)
    {
        Assert.Equal(error, Assert.Single(Validate(new() { SortBy = field, SortDirection = direction }).Errors).PropertyName);
    }

    [Theory]
    [InlineData("ParseRequest", "Succeeded")]
    [InlineData("Explain", "ProviderFailed")]
    [InlineData("Explain", "InvalidOutput")]
    [InlineData("ParseRequest", "Abandoned")]
    [InlineData("Explain", "InProgress")]
    public void C04_AllDocumentedKindAndStatusValuesAreValid(string kind, string status) =>
        Assert.True(Validate(new() { Kind = kind, Status = status }).IsValid);

    [Theory]
    [InlineData("Embedding", null, "Kind")]
    [InlineData("0", null, "Kind")]
    [InlineData("parseRequest", null, "Kind")]
    [InlineData(null, "Completed", "Status")]
    [InlineData(null, "Released", "Status")]
    [InlineData(null, "0", "Status")]
    public void C04_V01_UnknownEnumsAreRejected(string? kind, string? status, string field) =>
        Assert.Equal(field, Assert.Single(Validate(new() { Kind = kind, Status = status }).Errors).PropertyName);

    [Theory]
    [InlineData("2026-1-01", null, "From")]
    [InlineData("2026-02-30", null, "From")]
    [InlineData(null, "not-a-date", "To")]
    [InlineData(null, "2026-10-13T00:00:00Z", "To")]
    [InlineData("2026-10-14", "2026-10-13", "From")]
    public void C05_V01_DateErrorsHaveCorrectFields(string? from, string? to, string field) =>
        Assert.Equal(field, Assert.Single(Validate(new() { From = from, To = to }).Errors).PropertyName);

    [Theory]
    [InlineData(null, null)]
    [InlineData("0001-01-01", null)]
    [InlineData(null, "9999-12-31")]
    [InlineData("0001-01-01", "9999-12-31")]
    [InlineData("2025-01-01", "2027-12-31")]
    public void C05_C06_V02_NoDefaultMaximumRangeOrFutureRestriction(string? from, string? to) =>
        Assert.True(Validate(new() { From = from, To = to }).IsValid);

    [Fact]
    public void C07_SearchIsIgnoredWithoutMutatingRequest()
    {
        var request = new AdminAiCallsRequest { Search = new string('x', 5000) };
        Assert.True(Validate(request).IsValid);
        Assert.Equal(5000, request.Search.Length);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10000, 100)]
    public void PagingBoundariesAndNormalizedSortAccepted(int page, int size) =>
        Assert.True(Validate(new() { Page = page, PageSize = size, SortBy = " STARTEDAT ", SortDirection = " ASC " }).IsValid);

    private static FluentValidation.Results.ValidationResult Validate(AdminAiCallsRequest query) =>
        new AdminAiCallsRequestValidator().Validate(query);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    }
}
