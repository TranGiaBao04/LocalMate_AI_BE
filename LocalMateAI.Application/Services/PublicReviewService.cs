using FluentValidation;
using LocalMateAI.Application.DTOs.PublicReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Application.Services;

public sealed class PublicReviewService(
    IPublicReviewRepository repository,
    IValidator<PublicReviewQuery> validator,
    IMemoryCache cache,
    TimeProvider timeProvider) : IPublicReviewService
{
    public const string CacheKey = "public-reviews";
    public const int MinRating = 4;

    // API công khai, ai cũng gọi được ⇒ cache như PublicStatsService.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<PublicReviewsResult> GetAsync(
        PublicReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return new PublicReviewsResult(PublicReviewsResultStatus.InvalidQuery, ValidationErrors: validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));
        }

        var latest = await GetLatestAsync(cancellationToken);
        return new PublicReviewsResult(
            PublicReviewsResultStatus.Success,
            latest with { Items = latest.Items.Take(query.Limit).ToList() });
    }

    // Luôn đọc MaxLimit dòng rồi cắt theo limit, để mọi giá trị limit dùng chung một lần đọc DB.
    private async Task<PublicReviewsResponse> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out PublicReviewsResponse? cached) && cached is not null)
        {
            return cached;
        }

        // Lỗi DB ném ra ngoài nên không bị cache.
        var reviews = await repository.GetFeaturedAsync(MinRating, PublicReviewQuery.MaxLimit, cancellationToken);
        var response = new PublicReviewsResponse(
            reviews.Select(WithPositiveQuickTags).ToList(),
            timeProvider.GetUtcNow().UtcDateTime);
        cache.Set(CacheKey, response, CacheDuration);
        return response;
    }

    // Landing chỉ hiện nhãn khen, giữ thứ tự người dùng đã gửi; không còn nhãn nào ⇒ mảng rỗng.
    private static PublicReviewItem WithPositiveQuickTags(PublicReviewItem review) =>
        review with { QuickTags = review.QuickTags.Where(tag => ReviewQuickTags.Positive.Contains(tag)).ToList() };
}
