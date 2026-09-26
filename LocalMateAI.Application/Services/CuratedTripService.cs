using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class CuratedTripService(
    IUserRepository userRepository,
    ICuratedItineraryRepository curatedItineraryRepository,
    ITripRepository tripRepository,
    ITripDetailService tripDetailService,
    ICoordinatesValidationService coordinatesValidationService,
    ITripOriginResolverService tripOriginResolverService,
    TimeProvider timeProvider) : ICuratedTripService
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

        var vietnamNow = VietnamTime.Now(timeProvider);
        var travelMode = request?.TravelMode ?? TravelMode.Auto;
        var errors = new Dictionary<string, string[]>();
        AddError(errors, "PlannedDate", TripTimingRules.ValidatePlannedDate(request?.PlannedDate, vietnamNow));
        AddError(errors, "StartTime",
            TripTimingRules.ValidateStartTime(request?.PlannedDate, request?.StartTime, vietnamNow));
        if (!Enum.IsDefined(travelMode))
        {
            errors["TravelMode"] = ["Phương tiện không hợp lệ."];
        }

        if (errors.Count > 0)
        {
            return ApplyCuratedItineraryResult.Invalid(errors);
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return ApplyCuratedItineraryResult.MissingUser();
        }

        // Cùng ngưỡng vùng phục vụ với generate; chỉ kiểm tra khi user gửi toạ độ xuất phát.
        if (latitude.HasValue)
        {
            var origin = await tripOriginResolverService.ResolveAsync(latitude.Value, longitude!.Value, cancellationToken)
                ?? throw new InvalidOperationException("No metro stations found.");
            if (!origin.IsWithinServiceArea)
            {
                return ApplyCuratedItineraryResult.OutsideServiceArea();
            }
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

        var scheduleOrigin = latitude.HasValue ? new ScheduleOrigin(latitude.Value, longitude!.Value) : null;
        var windowError = TripTimingRules.ValidateWindow(
            request?.StartTime, CuratedTripBuilder.TotalMinutes(source, travelMode, scheduleOrigin));
        if (windowError is not null)
        {
            return ApplyCuratedItineraryResult.Invalid(new Dictionary<string, string[]> { ["StartTime"] = [windowError] });
        }

        var plannedStartAt = TripTimingRules.ResolveStart(request?.PlannedDate, request?.StartTime, vietnamNow);
        var trip = CuratedTripBuilder.Build(userId, source, latitude, longitude, plannedStartAt, travelMode);
        await tripRepository.AddAsync(trip, cancellationToken);

        var detail = await tripDetailService.GetAsync(userId, trip.Id, cancellationToken);
        return detail.Status == GetTripDetailResultStatus.Success
            ? ApplyCuratedItineraryResult.Succeeded(detail.Response!)
            : throw new InvalidOperationException("The created trip could not be read back.");
    }

    private static void AddError(Dictionary<string, string[]> errors, string field, string? error)
    {
        if (error is not null)
        {
            errors[field] = [error];
        }
    }
}
