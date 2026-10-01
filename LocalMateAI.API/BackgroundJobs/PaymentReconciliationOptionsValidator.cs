using LocalMateAI.Application.Payments;
using Microsoft.Extensions.Options;

namespace LocalMateAI.API.BackgroundJobs;

public sealed class PaymentReconciliationOptionsValidator : IValidateOptions<PaymentReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentReconciliationOptions options) =>
        options.BatchSize is >= 1 and <= 1000 && options.PollIntervalSeconds is >= 1 and <= 3600
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("PaymentReconciliation requires BatchSize 1..1000 and PollIntervalSeconds 1..3600.");
}
