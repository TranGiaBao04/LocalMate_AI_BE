using LocalMateAI.Application.DTOs.Metro;

namespace LocalMateAI.Application.Interfaces.Services;

/// <summary>Nguồn lịch tàu Metro đã được kiểm tra hợp lệ; nạp một lần lúc khởi động.</summary>
public interface IMetroTimetableSource
{
    MetroTimetable Timetable { get; }
}
