using LocalMateAI.Application.Payments;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Payments;

public sealed class PaymentEvidenceOptionsValidator : IValidateOptions<PaymentEvidenceOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentEvidenceOptions options) =>
        options.RawWebhookRetentionDays is >= 1 and <= 365 ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("PaymentEvidence:RawWebhookRetentionDays must be between 1 and 365.");
}
