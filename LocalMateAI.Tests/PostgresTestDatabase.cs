using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace LocalMateAI.Tests;

public static class PostgresTestDatabase
{
    public static string RequireConnection()
    {
        var connection = Environment.GetEnvironmentVariable("LOCALMATE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("PostgreSQL tests require LOCALMATE_TEST_CONNECTION (isolated test DB).");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (!parsed.Database!.StartsWith("localmate_s1b_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing non-isolated PostgreSQL test database.");
        return connection;
    }
    public static async Task ImportLegacyAsync(AppDbContext context, Guid userId)
    {
        foreach (var s in await context.UserSubscriptions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync())
            context.SubscriptionPeriods.Add(new SubscriptionPeriod
            {
                UserId = s.UserId,
                PlanId = SubscriptionBaseline.PlanId(s.PlanCode),
                PlanVersionId = SubscriptionBaseline.VersionId(s.PlanCode),
                StartsAt = s.StartsAt,
                EndsAt = s.EndsAt,
                LegacyUserSubscriptionId = s.Id
            });
        await context.SaveChangesAsync();
    }

    // Test nâng cấp tạo user khi DB còn ở mốc cũ: chỉ ghi các cột có từ AddRbacAndUserStatus,
    // để cột mới thêm vào Users sau này không làm vỡ các test đó.
    public static async Task InsertUserAsync(AppDbContext context, User user)
    {
        if (user.CreatedAt == default) user.CreatedAt = DateTime.UtcNow;
        if (user.UpdatedAt == default) user.UpdatedAt = user.CreatedAt;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Id","FullName","Email","PasswordHash","RoleId","Status","LockedAt","LockReason","CreatedAt","UpdatedAt")
            VALUES ({user.Id},{user.FullName},{user.Email},{user.PasswordHash},{user.RoleId},{user.Status.ToString()},
                    {user.LockedAt},{user.LockReason},{user.CreatedAt},{user.UpdatedAt})
            """);
    }

    // Test nâng cấp tạo trip khi DB còn ở mốc cũ: chỉ ghi các cột có từ AddRbacAndUserStatus,
    // để cột mới thêm vào Trips sau này (vd. StartStationId) không làm vỡ các test đó.
    public static async Task<Trip> InsertTripAsync(AppDbContext context, Guid userId)
    {
        var now = DateTime.UtcNow;
        var trip = new Trip
        {
            UserId = userId,
            Status = TripStatus.Draft,
            DurationHours = 3,
            StartLatitude = 10.77,
            StartLongitude = 106.69,
            BudgetMax = 300000,
            CreatedAt = now,
            UpdatedAt = now
        };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Trips" ("Id","UserId","StartLatitude","StartLongitude","DurationHours","BudgetMin","BudgetMax",
                                 "Status","TravelMode","CreatedAt","UpdatedAt")
            VALUES ({trip.Id},{trip.UserId},{trip.StartLatitude},{trip.StartLongitude},{trip.DurationHours},
                    {trip.BudgetMin},{trip.BudgetMax},{trip.Status.ToString()},{trip.TravelMode.ToString()},
                    {trip.CreatedAt},{trip.UpdatedAt})
            """);
        return trip;
    }
}
