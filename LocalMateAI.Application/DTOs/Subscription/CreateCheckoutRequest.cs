using System.Text.Json.Serialization;

namespace LocalMateAI.Application.DTOs.Subscription;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCheckoutRequest(string? PlanCode);
