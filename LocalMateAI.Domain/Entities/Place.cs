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
    public bool IsVerified { get; set; }

    // Xoá mềm bởi Admin: luôn đi kèm Status = Inactive để các truy vấn "Status = 'Active'" tự loại địa điểm này.
    // Dòng được giữ lại vì chặng lịch trình, lịch trình mẫu và đánh giá cũ vẫn tham chiếu tới nó.
    public DateTime? DeletedAt { get; set; }

    public decimal EstimatedCostMin { get; set; }
        public decimal EstimatedCostMax { get; set; }
        public string? ImageUrl { get; set; }

        public ICollection<PlaceTag> Tags { get; set; } = [];
    }
