using LocalMateAI.Domain.Common;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Domain.Entities;

public sealed class MetroStation : BaseEntity
{
    public required string Name { get; set; }
    public int Order { get; set; }
    public required Point Location { get; set; }
}
