using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripOriginResolverService(
    IGeoService geoService,
    IMetroStationRepository stationRepository,
    ICoordinatesValidationService coordinatesValidationService,
    IMetroTimetableSource metroTimetableSource,
    ISystemSettingProvider settings,
    TimeProvider timeProvider) : ITripOriginResolverService
{
    public async Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var stations = await stationRepository.GetAllAsync(cancellationToken);
        var travelsByMetro = request.TravelMode == TravelMode.Metro;

        MetroStationSummaryResponse? startStation = null;
        NearestStationResult? boardingStation;
        double startLatitude;
        double startLongitude;
        string? serviceAreaFailure = null;

        if (request.StartStationOrder is { } startStationOrder)
        {
            // Xuất phát từ ga: ga đó là ga lên, toạ độ xuất phát là toạ độ ga, luôn trong vùng phục vụ.
            startStation = stations.FirstOrDefault(station => station.Order == startStationOrder);
            if (startStation is null)
            {
                return null;
            }

            startLatitude = startStation.Latitude;
            startLongitude = startStation.Longitude;
            boardingStation = new NearestStationResult(
                startStation.Id, startStation.Name, startStation.Latitude, startStation.Longitude, DistanceMeters: 0);
        }
        else
        {
            if (request.StartLatitude is not { } latitude || request.StartLongitude is not { } longitude)
            {
                throw new ArgumentException(
                    "The trip request must be validated before its origin is resolved.", nameof(request));
            }

            startLatitude = latitude;
            startLongitude = longitude;
            boardingStation = await geoService.FindNearestStationAsync(latitude, longitude, cancellationToken);
            if (boardingStation is null)
            {
                return null;
            }

            // Đi xe hay đi bộ thì không xét khoảng cách tới ga: thời gian đi tới chặng đầu đã tính vào số giờ rảnh,
            // chỉ chặn toạ độ nằm ngoài TP.HCM. Riêng đi Metro phải ra được ga lên, nên có ngưỡng khoảng cách
            // đường chim bay do admin chỉnh trong SystemSettings (mặc định 12 km).
            if (!coordinatesValidationService.IsValidHcmcCoordinate(latitude, longitude))
            {
                serviceAreaFailure = TripInsufficiencyReasons.OutOfServiceArea;
            }
            else if (travelsByMetro)
            {
                var maxDistanceMeters = await settings.GetIntAsync(
                    SystemSettingKeys.MaxServiceAreaDistanceMeters, cancellationToken);
                if (boardingStation.DistanceMeters > maxDistanceMeters)
                {
                    serviceAreaFailure = TripInsufficiencyReasons.TooFarFromStationForMetro;
                }
            }
        }

        MetroStationSummaryResponse? destinationStation = null;
        if (request.DestinationStationOrder is { } destinationStationOrder)
        {
            destinationStation = stations.FirstOrDefault(station => station.Order == destinationStationOrder);
            if (destinationStation is null)
            {
                return null;
            }
        }

        var boardingStationSummary = stations.FirstOrDefault(station => station.Id == boardingStation.StationId);
        var anchorStation = destinationStation ?? boardingStationSummary;
        if (anchorStation is null || boardingStationSummary is null)
        {
            return null;
        }

        MetroBoarding? metroBoarding = null;
        if (travelsByMetro)
        {
            // Lịch tàu theo ngày đi (thứ trong tuần quyết định giờ chuyến cuối); bỏ trống ngày = hôm nay giờ Việt Nam.
            var plannedDate = request.PlannedDate ?? DateOnly.FromDateTime(VietnamTime.Now(timeProvider));
            metroBoarding = new MetroBoarding(
                boardingStationSummary.Order,
                boardingStation.DistanceMeters,
                StartsAtStation: startStation is not null,
                MetroDayTimetable.For(metroTimetableSource.Timetable, plannedDate));
        }

        return new TripOriginResolution(
            startLatitude,
            startLongitude,
            boardingStation,
            anchorStation,
            startStation?.Id,
            destinationStation?.Id,
            serviceAreaFailure,
            metroBoarding);
    }
}
