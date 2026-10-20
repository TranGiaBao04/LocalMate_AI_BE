using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AppFeedbackRepository(AppDbContext dbContext) : IAppFeedbackRepository
{
    private static readonly SortMap<AppFeedbackRow> SortMap =
        new SortMap<AppFeedbackRow>("createdAt", true, row => row.Id)
            .Add("createdAt", row => row.CreatedAt)
            .Add("rating", row => row.Rating);

    public async Task<int> CountUserSubmissionsSinceUtcAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        return await dbContext.AppFeedbacks
            .AsNoTracking()
            .CountAsync(f => f.UserId == userId && f.CreatedAt >= sinceUtc, cancellationToken);
    }

    public async Task<AppFeedback> CreateAsync(AppFeedback feedback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        dbContext.AppFeedbacks.Add(feedback);
        await dbContext.SaveChangesAsync(cancellationToken);
        return feedback;
    }

    public async Task<PagedResult<AdminAppFeedbackItemResponse>> GetAdminListAsync(
        AdminAppFeedbackQuery query,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows =
            from feedback in dbContext.AppFeedbacks.AsNoTracking()
            join user in dbContext.Users on feedback.UserId equals user.Id
            select new AppFeedbackRow
            {
                Id = feedback.Id,
                Type = feedback.Type,
                Rating = feedback.Rating,
                Content = feedback.Content,
                PagePath = feedback.PagePath,
                UserAgent = feedback.UserAgent,
                CreatedAt = feedback.CreatedAt,
                UserId = user.Id,
                UserFullName = user.FullName,
                UserEmail = user.Email
            };

        if (query.Type.HasValue)
        {
            rows = rows.Where(r => r.Type == query.Type.Value);
        }

        if (query.Rating.HasValue)
        {
            rows = rows.Where(r => r.Rating == query.Rating.Value);
        }

        if (query.UserId.HasValue)
        {
            rows = rows.Where(r => r.UserId == query.UserId.Value);
        }

        if (fromUtc.HasValue)
        {
            rows = rows.Where(r => r.CreatedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            rows = rows.Where(r => r.CreatedAt < toUtc.Value);
        }

        if (query.NormalizedSearch is { } search)
        {
            var pattern = LikePattern(search);
            rows = rows.Where(row =>
                EF.Functions.ILike(EF.Functions.Unaccent(row.Content), EF.Functions.Unaccent(pattern), "\\")
                || EF.Functions.ILike(EF.Functions.Unaccent(row.UserFullName), EF.Functions.Unaccent(pattern), "\\")
                || EF.Functions.ILike(EF.Functions.Unaccent(row.UserEmail), EF.Functions.Unaccent(pattern), "\\"));
        }

        return await rows
            .ApplySort(query, SortMap)
            .Select(r => new AdminAppFeedbackItemResponse(
                r.Id,
                r.Type,
                r.Rating,
                r.Content,
                r.PagePath,
                r.UserAgent,
                r.CreatedAt,
                new AppFeedbackUserDto(r.UserId, r.UserFullName, r.UserEmail)))
            .ToPagedResultAsync(query, cancellationToken);
    }

    private static string LikePattern(string search) =>
        "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private sealed class AppFeedbackRow
    {
        public Guid Id { get; init; }
        public AppFeedbackType Type { get; init; }
        public int? Rating { get; init; }
        public string Content { get; init; } = string.Empty;
        public string? PagePath { get; init; }
        public string? UserAgent { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid UserId { get; init; }
        public string UserFullName { get; init; } = string.Empty;
        public string UserEmail { get; init; } = string.Empty;
    }
}
