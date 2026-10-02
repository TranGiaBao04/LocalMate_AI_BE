namespace LocalMateAI.Application.Payments;

// Monetary eligibility is supplied by the caller after checking persisted purchase provenance.
public sealed record UpgradeCreditSource(Guid PeriodId, DateTime StartsAt, DateTime EndsAt,
    DateTime? TerminatedAt, decimal PurchasedPrice, int PurchasedDurationDays, bool MonetaryCreditEligible);

public sealed record UpgradeCreditInput(decimal TargetPrice, DateTime NowUtc,
    IReadOnlyList<UpgradeCreditSource> Sources);

public sealed record UpgradeCreditSourceResult(Guid PeriodId, int RemainingDays, decimal CalculatedCreditAmount);

public sealed record UpgradeCreditResult(decimal ListPrice, decimal RawCreditTotal, decimal RoundedCreditTotal,
    decimal AppliedCredit, decimal AmountPayable, IReadOnlyList<UpgradeCreditSourceResult> Sources);
