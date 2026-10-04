using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Matching;

namespace LocalMateAI.Application.Services;

/// <summary>Khoá xếp hạng ứng viên dùng chung cho /match, /generate và /feasibility-check.</summary>
public static class CandidateRanking
{
    /// <summary>
    /// Khoảng cách đường chim bay (km) từ địa điểm tới ga cột mốc. Địa điểm gần ga cột mốc được ưu tiên,
    /// kể cả khi nó thuộc cụm của ga kề.
    /// </summary>
    public static double DistanceToAnchorKm(PlaceCandidateDto candidate, MetroStationSummaryResponse anchorStation) =>
        TravelTimeEstimator.HaversineKm(
            candidate.Latitude, candidate.Longitude, anchorStation.Latitude, anchorStation.Longitude);
}
