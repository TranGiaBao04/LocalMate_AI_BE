using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Domain.Entities;

public sealed class Place : BaseEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Address { get; set; }
    public required Point Location { get; set; }
    public PlaceCategory Category { get; set; }
    public PlaceStatus Status { get; set; } = PlaceStatus.Pending;
    public decimal EstimatedCostMin { get; set; }
    public decimal EstimatedCostMax { get; set; }
    public string? ImageUrl { get; set; }
}
