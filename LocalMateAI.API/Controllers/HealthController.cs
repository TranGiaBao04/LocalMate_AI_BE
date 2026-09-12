using LocalMateAI.Application.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(AppDbContext dbContext, IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// Kiểm tra trạng thái ứng dụng API
    /// </summary>
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "Online",
            timestamp = DateTime.UtcNow,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }

    /// <summary>
    /// Kiểm tra kết nối tới Cơ sở dữ liệu PostgreSQL / PostGIS
    /// </summary>
    [HttpGet("db")]
    public async Task<IActionResult> CheckDatabaseConnection()
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync();

            if (canConnect)
            {
                return Ok(new
                {
                    status = "Connected",
                    database = dbContext.Database.GetDbConnection().Database,
                    dataSource = dbContext.Database.GetDbConnection().DataSource,
                    message = "Kết nối CSDL PostgreSQL / PostGIS thành công!"
                });
            }

            return StatusCode(503, new
            {
                status = "Disconnected",
                message = "Không thể kết nối CSDL. Vui lòng kiểm tra Docker Postgres/PostGIS đã chạy chưa.",
                connectionString = MaskConnectionString(connectionString)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "Error",
                error = ex.Message,
                message = "Lỗi khi thử kết nối CSDL PostgreSQL.",
                connectionString = MaskConnectionString(connectionString)
            });
        }
    }

    private static string MaskConnectionString(string? connStr)
    {
        if (string.IsNullOrEmpty(connStr)) return "Chưa cấu hình DefaultConnection trong appsettings!";
        // Che bớt password nếu có
        return System.Text.RegularExpressions.Regex.Replace(connStr, @"Password=([^;]+)", "Password=*****");
    }
}
