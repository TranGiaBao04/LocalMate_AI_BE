using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LocalMateAI.API.Middlewares;

// Giới hạn số lần gọi các API OTP theo địa chỉ IP, chặn việc dùng API để spam mail tới nhiều email khác nhau.
public static class OtpRateLimitPolicy
{
    public const string Name = "otp";
    public const int PermitLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Lấy IP của kết nối trực tiếp. Khi deploy sau proxy/load balancer phải bật ForwardedHeaders,
    // nếu không mọi người dùng sẽ chung IP của proxy.
    public static RateLimitPartition<string> Partition(HttpContext httpContext) =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0
            });

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : (int)Window.TotalSeconds;

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = "Bạn thao tác quá nhanh, vui lòng thử lại sau.",
            Type = "https://httpstatuses.com/429",
            Instance = httpContext.Request.Path
        };

        problem.Extensions["code"] = "too_many_requests";
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}
