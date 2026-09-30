using LocalMateAI.Domain.Enums;
namespace LocalMateAI.Application.Services;

public static class PlanIdentity
{
    public const string Free = "FREE";
    public const string TripPass = "TRIP_PASS";
    public const string Membership = "MEMBERSHIP";
    public static string? Canonical(string? code) => code switch
    {
        "Free" => Free,
        "TripPass" => TripPass,
        "Membership" => Membership,
        _ => code
    };
    public static string PublicCode(string code) => code switch
    {
        Free => "Free",
        TripPass => "TripPass",
        Membership => "Membership",
        _ => code
    };
    public static PlanCode? Legacy(string code) => code switch
    {
        Free => PlanCode.Free,
        TripPass => PlanCode.TripPass,
        Membership => PlanCode.Membership,
        _ => null
    };
}
