namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>Địa điểm có mở cửa suốt lúc tham quan một chặng không (tính lúc đọc theo giờ mở cửa hiện tại).</summary>
public enum PlaceOpeningStatus
{
    Open,
    Closed,
    Unknown
}
