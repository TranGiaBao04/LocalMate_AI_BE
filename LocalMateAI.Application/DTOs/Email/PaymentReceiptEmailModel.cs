namespace LocalMateAI.Application.DTOs.Email;

// Dữ liệu biên nhận thanh toán, đã định dạng sẵn để template chỉ việc hiển thị.
public sealed record PaymentReceiptEmailModel(
    string FullName,
    string OrderCode,
    string PlanName,
    string TypeLabel,
    string Amount,
    string PaidAt,
    string AddedDays,
    string ValidUntil);
