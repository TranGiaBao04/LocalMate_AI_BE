using System.Globalization;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Luật giờ mở cửa của địa điểm, thuần: kiểm tra dữ liệu admin gửi, đọc ô giờ của file import, dựng response đủ 7 thứ.
/// </summary>
public static class PlaceOpeningHourRules
{
    // Thứ tự hiển thị cho admin: Thứ 2 → Chủ nhật.
    private static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    public static IReadOnlyList<DayOfWeek> WeekOrder => Week;

    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];
    private static readonly char[] RangeSeparators = ['-', '–', '—'];

    // Cách ghi "mở cả ngày" trong file import, đổi thành 00:00–00:00.
    private static readonly string[] AllDayTexts = ["24h", "24/24"];

    private static readonly Dictionary<string, DayOfWeek> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mon"] = DayOfWeek.Monday,
        ["Tue"] = DayOfWeek.Tuesday,
        ["Wed"] = DayOfWeek.Wednesday,
        ["Thu"] = DayOfWeek.Thursday,
        ["Fri"] = DayOfWeek.Friday,
        ["Sat"] = DayOfWeek.Saturday,
        ["Sun"] = DayOfWeek.Sunday
    };

    /// <summary>Kiểm tra request của admin; hợp lệ thì trả các dòng đã sắp Monday → Sunday.</summary>
    public static (IReadOnlyList<PlaceOpeningHourDto>? Days, Dictionary<string, string[]> Errors) Validate(
        SavePlaceOpeningHoursRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Days is null)
        {
            errors["Days"] = ["Danh sách giờ mở cửa là bắt buộc (gửi danh sách rỗng để xoá hết)."];
            return (null, errors);
        }

        if (request.Days.Count > Week.Length)
        {
            errors["Days"] = ["Tối đa 7 dòng, mỗi thứ một dòng."];
            return (null, errors);
        }

        var days = new List<PlaceOpeningHourDto>();
        var seen = new HashSet<DayOfWeek>();

        for (var index = 0; index < request.Days.Count; index++)
        {
            var item = request.Days[index];
            var prefix = $"Days[{index}]";

            if (item.DayOfWeek is not { } day || !Enum.IsDefined(day))
            {
                errors[$"{prefix}.DayOfWeek"] = ["Thứ là bắt buộc (Monday … Sunday)."];
                continue;
            }

            if (!seen.Add(day))
            {
                errors[$"{prefix}.DayOfWeek"] = [$"{day} bị lặp lại."];
                continue;
            }

            if (item.IsClosed is not { } isClosed)
            {
                errors[$"{prefix}.IsClosed"] = ["IsClosed là bắt buộc."];
                continue;
            }

            // Dùng lại luật của entity (đóng cả ngày thì không có giờ, giờ mở khác giờ đóng...).
            var entity = new PlaceOpeningHour
            {
                DayOfWeek = day,
                IsClosed = isClosed,
                OpenTime = item.OpenTime,
                CloseTime = item.CloseTime
            };
            if (!entity.Validate(out var error))
            {
                errors[prefix] = [error];
                continue;
            }

            days.Add(new PlaceOpeningHourDto(day, isClosed, item.OpenTime, item.CloseTime));
        }

        return errors.Count > 0
            ? (null, errors)
            : (days.OrderBy(day => Position(day.DayOfWeek)).ToList(), errors);
    }

    /// <summary>
    /// Đọc ô "giờ mở cửa" của file import. Trống ⇒ không có giờ, không cảnh báo.
    /// Hiểu "HH:mm-HH:mm" (cả tuần), "Mon-Sun HH:mm-HH:mm" (khoảng thứ, được vòng qua CN, thứ ngoài khoảng = nghỉ),
    /// "Sat HH:mm-HH:mm" (một thứ). Mở cả ngày ghi "24h", "24/24", "00:00-00:00" hoặc "00:00-24:00" (⇒ 00:00–00:00),
    /// dùng được cả với phần thứ ("Mon-Fri 24h"). Không đọc được ⇒ không có giờ + Warning, KHÔNG tự điền giờ.
    /// </summary>
    public static (IReadOnlyList<PlaceOpeningHourDto> Days, string? Warning) ParseImportText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ([], null);
        }

        var text = raw.Trim();
        // Phần thứ (nếu có) đứng đầu và bắt đầu bằng chữ cái; "6:00 – 21:00" có dấu cách nhưng không có phần thứ.
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (dayPart, timePart) = parts.Length == 2 && char.IsLetter(parts[0][0])
            ? (parts[0], parts[1])
            : ((string?)null, text);

        var openDays = dayPart is null ? Week.ToHashSet() : ParseDays(dayPart);
        if (openDays is null || !TryParseTimeRange(timePart, out var open, out var close))
        {
            return ([], $"Không đọc được giờ mở cửa '{text}'. Địa điểm sẽ được tạo không có giờ mở cửa.");
        }

        var days = Week
            .Select(day => openDays.Contains(day)
                ? new PlaceOpeningHourDto(day, false, open, close)
                : new PlaceOpeningHourDto(day, true, null, null))
            .ToList();
        return (days, null);
    }

    /// <summary>Đủ 7 thứ Monday → Sunday; thứ không có dòng thì để trống.</summary>
    public static PlaceOpeningHoursResponse ToResponse(Guid placeId, IReadOnlyList<PlaceOpeningHourDto> stored)
    {
        var byDay = stored.ToDictionary(day => day.DayOfWeek);
        return new PlaceOpeningHoursResponse(
            placeId,
            stored.Count > 0,
            Week
                .Select(day => byDay.TryGetValue(day, out var found)
                    ? found
                    : new PlaceOpeningHourDto(day, false, null, null))
                .ToList());
    }

    private static int Position(DayOfWeek day) => Array.IndexOf(Week, day);

    // "Mon-Sun", "Fri-Mon" (vòng qua CN) hoặc "Sat". Sai ⇒ null.
    private static HashSet<DayOfWeek>? ParseDays(string text)
    {
        var range = text.Split(RangeSeparators, StringSplitOptions.TrimEntries);
        if (range.Length is < 1 or > 2 || !range.All(DayNames.ContainsKey))
        {
            return null;
        }

        var from = Position(DayNames[range[0]]);
        var to = Position(DayNames[range[^1]]);
        var count = (to - from + Week.Length) % Week.Length + 1;
        return Enumerable.Range(0, count).Select(offset => Week[(from + offset) % Week.Length]).ToHashSet();
    }

    private static bool TryParseTimeRange(string text, out TimeOnly open, out TimeOnly close)
    {
        open = close = TimeOnly.MinValue;
        if (AllDayTexts.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var range = text.Split(RangeSeparators, StringSplitOptions.TrimEntries);
        if (range.Length != 2 || !TryParseTime(range[0], out open))
        {
            return false;
        }

        // "24:00" ở giờ đóng nghĩa là nửa đêm: "18:00-24:00" = 18:00–00:00, "00:00-24:00" = mở cả ngày.
        if (range[1] != "24:00" && !TryParseTime(range[1], out close))
        {
            return false;
        }

        // Giờ trùng chỉ hợp lệ khi là 00:00–00:00 (mở cả ngày).
        return open != close || open == TimeOnly.MinValue;
    }

    private static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
