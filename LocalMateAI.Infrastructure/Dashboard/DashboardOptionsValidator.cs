using LocalMateAI.Application.Dashboard;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Dashboard;

public sealed class DashboardOptionsValidator : IValidateOptions<DashboardOptions>
{
    public ValidateOptionsResult Validate(string? name, DashboardOptions options) =>
        options.BreakEvenMonthlyRevenue > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Dashboard:BreakEvenMonthlyRevenue phải lớn hơn 0 (VNĐ).");
}
