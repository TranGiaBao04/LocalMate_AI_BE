using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.Itineraries;

/// <summary>Luật phân trang chung cho danh sách lịch trình mẫu của admin (không có bộ lọc riêng).</summary>
public sealed class AdminCuratedItineraryQueryValidator : PagedQueryValidator<AdminCuratedItineraryQuery>
{
    public AdminCuratedItineraryQueryValidator() : base(AdminCuratedItineraryQuery.SortFields)
    {
    }
}
