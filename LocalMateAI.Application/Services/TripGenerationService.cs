using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripGenerationService(
    IUserRepository userRepository,
    ITagRepository tagRepository,
    IHeuristicFallbackEngine heuristicFallbackEngine,
    ITripRepository tripRepository,
    ITripDetailService tripDetailService) : ITripGenerationService
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

        // Kiểm tra sau bước validate của engine nên TagIds chắc chắn không null.
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

        var trip = GeneratedTripBuilder.Build(userId, request, payload.Stops, tagIds);
        await tripRepository.AddAsync(trip, cancellationToken);

        var detail = await tripDetailService.GetAsync(userId, trip.Id, cancellationToken);
        return detail.Status == GetTripDetailResultStatus.Success
            ? GenerateTripResult.Succeeded(detail.Response!)
            : throw new InvalidOperationException("The generated trip could not be read back.");
    }
}
