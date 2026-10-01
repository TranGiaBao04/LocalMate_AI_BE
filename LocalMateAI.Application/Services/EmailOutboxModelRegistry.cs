using System.Text.Json;
using LocalMateAI.Application.DTOs.Email;

namespace LocalMateAI.Application.Services;

// Ánh xạ tên template → kiểu model, để job đọc lại Model (JSON) trong outbox thành đúng record đã dùng khi xếp mail.
// Thêm 1 dòng vào Default mỗi khi có loại mail mới đi qua outbox.
public sealed class EmailOutboxModelRegistry(IReadOnlyDictionary<string, Type> modelTypes)
{
    public static EmailOutboxModelRegistry Default { get; } = new(new Dictionary<string, Type>
    {
        [EmailTemplateNames.PaymentReceipt] = typeof(PaymentReceiptEmailModel),
        [EmailTemplateNames.SingleItineraryPaymentReceipt] = typeof(SingleItineraryPaymentReceiptEmailModel),
        [EmailTemplateNames.TripItinerary] = typeof(TripItineraryEmailModel)
    });

    public static string Serialize<TModel>(TModel model)
        where TModel : notnull =>
        JsonSerializer.Serialize(model);

    // Null nếu template chưa đăng ký. Ném JsonException nếu Model không đọc được.
    public object? Deserialize(string templateName, string modelJson) =>
        modelTypes.TryGetValue(templateName, out var modelType)
            ? JsonSerializer.Deserialize(modelJson, modelType)
            : null;
}
