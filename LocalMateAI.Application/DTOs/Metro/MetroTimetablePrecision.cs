namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>
/// Exact: giờ đã đối chiếu thực tế (≥ 18/20 chuyến lệch ≤ 2 phút), FE được hiện giờ từng chuyến.
/// Headway: chưa đối chiếu, FE chỉ nên hiện "khoảng X phút/chuyến".
/// </summary>
public enum MetroTimetablePrecision
{
    Headway,
    Exact
}
