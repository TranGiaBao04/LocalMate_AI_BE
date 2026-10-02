using System.Text.Json.Serialization;
namespace LocalMateAI.Application.DTOs.Trips;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinalizeTripRequest
{
    public string FundingSource { get; init; } = "Normal";
    public Guid? EntitlementId { get; init; }
    [JsonIgnore]
    public bool IsValid => FundingSource == "Normal" ? EntitlementId is null
        : FundingSource == "SingleEntitlement" && EntitlementId is { } id && id != Guid.Empty;
}
