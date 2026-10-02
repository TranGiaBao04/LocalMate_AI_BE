namespace LocalMateAI.Application.DTOs.Email;

public sealed record UpgradePaymentReceiptEmailModel(string FullName, string OrderCode, string PlanName,
    string TypeLabel, string ListPrice, string CreditAmount, string Amount, string PaidAt,
    string Duration, string EffectiveFrom, string ValidUntil);
