using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

/// <summary>Một lần gọi LLM cho một người dùng. Dùng để đếm trần số lần gọi và theo dõi chi phí.</summary>
public sealed class LlmCallLog : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? TripId { get; set; }
    public LlmCallKind Kind { get; set; }
    public required string Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int DurationMilliseconds { get; set; }
    public LlmCallOutcome Outcome { get; set; }
}
