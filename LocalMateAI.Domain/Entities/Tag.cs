using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class Tag : BaseEntity
{
    public required string Name { get; set; }

    public TagType Type { get; set; }

    public bool IsActive { get; set; } = true;
}
