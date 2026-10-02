using System.Text.Json.Serialization;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Feedback;

public sealed record FeedbackResponse(
    Guid Id,
    Guid TripId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FeedbackQuickTag>))]
    FeedbackQuickTag QuickTag,
    string? Comment,
    DateTime CreatedAt);
