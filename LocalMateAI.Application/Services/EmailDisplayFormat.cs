using System.Globalization;

namespace LocalMateAI.Application.Services;

// Định dạng số liệu hiển thị trong email (template chỉ in ra, không tự định dạng).
// Tự khai báo dấu phân cách để không phụ thuộc culture vi-VN có trên máy chủ hay không.
public static class EmailDisplayFormat
{
    private static readonly NumberFormatInfo VietnameseNumberFormat = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ","
    };

    // 59000 → "59.000đ"
    public static string Money(decimal amount) =>
        $"{amount.ToString("#,0", VietnameseNumberFormat)}đ";

    // 45 → "45 phút", 60 → "1 giờ", 145 → "2 giờ 25 phút"
    public static string Duration(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? $"{rest} phút"
            : rest == 0 ? $"{hours} giờ"
            : $"{hours} giờ {rest} phút";
    }

    // Thời điểm UTC → giờ Việt Nam "dd/MM/yyyy HH:mm".
    public static string VietnamDateTime(DateTime utc) =>
        (DateTime.SpecifyKind(utc, DateTimeKind.Utc) + VietnamTime.Offset)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    // Thời điểm UTC → ngày theo giờ Việt Nam "dd/MM/yyyy".
    public static string VietnamDate(DateTime utc) =>
        (DateTime.SpecifyKind(utc, DateTimeKind.Utc) + VietnamTime.Offset)
            .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
