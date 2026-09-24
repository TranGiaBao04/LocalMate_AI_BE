using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class CuratedTripService(
    IUserRepository userRepository,
    ICuratedItineraryRepository curatedItineraryRepository,
    ITripRepository tripRepository,
    ITripDetailService tripDetailService,
    ICoordinatesValidationService coordinatesValidationService) : ICuratedTripService
{
    public async Task<ApplyCuratedItineraryResult> ApplyAsync(
        Guid userId,
        Guid curatedItineraryId,
        ApplyCuratedItineraryRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (curatedItineraryId == Guid.Empty)
        {
            return ApplyCuratedItineraryResult.InvalidCuratedId();
        }

        var latitude = request?.StartLatitude;
        var longitude = request?.StartLongitude;

        // Phải gửi cả hai hoặc không gửi; toạ độ gửi lên phải nằm trong TP.HCM.
        if (latitude.HasValue != longitude.HasValue
            || (latitude.HasValue
                && !coordinatesValidationService.ValidateCoordinate(latitude.Value, longitude!.Value).IsValid))
        {
            return ApplyCuratedItineraryResult.InvalidStartLocation();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return ApplyCuratedItineraryResult.MissingUser();
        }

        var source = await curatedItineraryRepository.GetForApplyAsync(curatedItineraryId, cancellationToken);
        if (source is null)
        {
            return ApplyCuratedItineraryResult.MissingItinerary();
        }

        if (source.Places.Count == 0)
        {
            return ApplyCuratedItineraryResult.UnavailableItinerary();
        }

        var trip = CuratedTripBuilder.Build(userId, source, latitude, longitude, request?.StartTime);
        await tripRepository.AddAsync(trip, cancellationToken);

        var detail = await tripDetailService.GetAsync(userId, trip.Id, cancellationToken);
        return detail.Status == GetTripDetailResultStatus.Success
            ? ApplyCuratedItineraryResult.Succeeded(detail.Response!)
            : throw new InvalidOperationException("The created trip could not be read back.");
    }
}
