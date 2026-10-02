namespace LocalMateAI.Application.Dashboard;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>Doanh thu cần đạt mỗi tháng để hoà vốn (VNĐ đầy đủ). Hiện là số tạm, chưa có chi phí vận hành thật.</summary>
    public decimal BreakEvenMonthlyRevenue { get; set; }
}
