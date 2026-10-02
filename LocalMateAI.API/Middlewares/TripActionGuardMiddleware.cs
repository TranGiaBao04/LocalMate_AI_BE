using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Middlewares;

/// <summary>
/// BE-58: Khóa quyền sửa timeline của trip đã chốt (Finalized).
/// Chặn mutation /api/trips/{tripId}/... (trừ finalize/fork) trả 409 trip_finalized.
/// </summary>
public sealed class TripActionGuardMiddleware(
    RequestDelegate next,
    ILogger<TripActionGuardMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, ITripRepository tripRepository)
    {
        var request = httpContext.Request;
        if (!TripActionGuardPolicy.ShouldGuard(request.Method, request.Path, out var tripId))
        {
            await next(httpContext);
            return;
        }

        var trip = await tripRepository.GetByIdAsync(tripId!.Value, httpContext.RequestAborted);
        if (trip is null || trip.Status != TripStatus.Finalized)
        {
            await next(httpContext); // trip không tồn tại → endpoint xử lý 404; chưa chốt → cho phép
            return;
        }

        logger.LogWarning("Blocked mutation on finalized trip {TripId}", trip.Id);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Trip is finalized and its timeline is locked.",
            Type = "https://httpstatuses.com/409",
            Instance = request.Path
        };
        problem.Extensions["code"] = "trip_finalized";

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(problem, httpContext.RequestAborted);
    }
}