using System.Globalization;
using System.Text;
using LocalMateAI.Application.DTOs.Payments;

namespace LocalMateAI.Application.Payments;

public static class AdminTransactionCsv
{
    public const string Header = "OrderId,ProviderOrderCode,CreatedAtUtc,PaidAtUtc,ExpiresAtUtc,UserId,FullName,Email,ProductKind,PlanCode,PlanName,OperationType,Status,Amount,CreditAmount,Currency";

    public static byte[] Write(IReadOnlyList<AdminTransactionResponse> rows)
    {
        var text = new StringBuilder().Append(Header).Append("\r\n");
        foreach (var row in rows)
        {
            string[] cells = [row.Id.ToString(), row.ProviderOrderCode.ToString(CultureInfo.InvariantCulture),
                Timestamp(row.CreatedAt), Timestamp(row.PaidAt), Timestamp(row.ExpiresAt), row.UserId.ToString(),
                SafeText(row.UserFullName), SafeText(row.UserEmail), row.ProductKind,
                SafeText(row.PlanCode), SafeText(row.PlanName), row.OperationType, row.Status,
                row.Amount.ToString("0", CultureInfo.InvariantCulture),
                row.CreditAmount.ToString("0", CultureInfo.InvariantCulture), row.Currency];
            text.AppendJoin(',', cells.Select(Escape)).Append("\r\n");
        }
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
    }

    private static string Timestamp(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? "";
    private static string SafeText(string? value) => string.IsNullOrEmpty(value) ? ""
        : value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
