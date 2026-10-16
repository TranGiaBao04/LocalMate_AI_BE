using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class PlaceOpeningHour : BaseEntity
{
    public Guid PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    // null khi IsClosed = true (đóng cả ngày)
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }

    public bool IsClosed { get; set; }

    public bool Validate(out string error)
    {
        if (IsClosed)
        {
            if (OpenTime is not null || CloseTime is not null)
            {
                error = "Đóng cả ngày thì không được có giờ mở/đóng.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (OpenTime is null || CloseTime is null)
        {
            error = "Phải có cả giờ mở và giờ đóng.";
            return false;
        }

        // 00:00–00:00 = mở cả ngày (24 giờ). Giờ trùng khác dễ là gõ nhầm nên vẫn từ chối.
        if (OpenTime == CloseTime && OpenTime != TimeOnly.MinValue)
        {
            error = "Giờ mở và giờ đóng không được bằng nhau (mở cả ngày thì nhập 00:00–00:00).";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
