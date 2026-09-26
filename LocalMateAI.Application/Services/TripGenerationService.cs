using FluentValidation;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripGenerationService(
    IUserRepository userRepository,
    IValidator<TripRequestDto> requestValidator,
    ITagRepository tagRepository,
    IHeuristicFallbackEngine heuristicFallbackEngine,
    ITripRepository tripRepository,
    ITripDetailService tripDetailService,
    TimeProvider timeProvider) : ITripGenerationService
{
    public async Task<GenerateTripResult> GenerateAsync(
        Guid userId,
        TripRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return GenerateTripResult.MissingUser();
        }

        // Validate trước để lỗi đầu vào/ngày/giờ trả về ngay, không phải chạy matching. Engine vẫn validate lại
        // (rẻ, không chạm DB) vì nó cũng được gọi từ /match và /fallback-itinerary.
        var validation = await requestValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return GenerateTripResult.Invalid(ToValidationErrors(validation));
        }

        // Sau validate nên TagIds chắc chắn không null. Kiểm tra tag trước matching: tag sai thì báo ngay,
        // không tốn một lượt matching và điểm khớp không bị tính lặng lẽ bằng 0.
        var tagIds = request.TagIds.Distinct().ToList();
        if (tagIds.Count > 0)
        {
            var activeTagIds = (await tagRepository.GetByIdsAsync(tagIds, cancellationToken))
                .Where(tag => tag.IsActive)
                .Select(tag => tag.Id)
                .ToHashSet();
            if (tagIds.Any(tagId => !activeTagIds.Contains(tagId)))
            {
                return GenerateTripResult.UnknownTags();
            }
        }

        // Khi có LLM (BE-35) sẽ thử LLM trước ở đây rồi rơi về heuristic nếu lỗi.
        var fallback = await heuristicFallbackEngine.GenerateFallbackAsync(request, "heuristic", cancellationToken);
        if (fallback.Status == FallbackItineraryStatus.ValidationFailed)
        {
            return GenerateTripResult.Invalid(fallback.ValidationErrors!);
        }

        var payload = fallback.Payload!;
        if (!payload.IsSufficient || payload.Stops.Count == 0)
        {
            return GenerateTripResult.NoSuitablePlaces(payload.InsufficiencyReason ?? "InsufficientCandidates");
        }

        var plannedStartAt = TripTimingRules.ResolveStart(
            request.PlannedDate, request.StartTime, VietnamTime.Now(timeProvider));
        var trip = GeneratedTripBuilder.Build(userId, request, payload.Stops, tagIds, plannedStartAt);
        await tripRepository.AddAsync(trip, cancellationToken);

        var detail = await tripDetailService.GetAsync(userId, trip.Id, cancellationToken);
        return detail.Status == GetTripDetailResultStatus.Success
            ? GenerateTripResult.Succeeded(detail.Response!)
            : throw new InvalidOperationException("The generated trip could not be read back.");
    }

    private static IReadOnlyDictionary<string, string[]> ToValidationErrors(
        FluentValidation.Results.ValidationResult validationResult) =>
        validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
}
