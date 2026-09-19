namespace LocalMateAI.Application.DTOs.Places;

public enum DeleteAdminPlaceResultStatus
{
    Success,
    NotFound,
    InUse
}

public sealed record DeleteAdminPlaceResult(DeleteAdminPlaceResultStatus Status)
{
    public static DeleteAdminPlaceResult Succeeded() =>
        new(DeleteAdminPlaceResultStatus.Success);

    public static DeleteAdminPlaceResult Missing() =>
        new(DeleteAdminPlaceResultStatus.NotFound);

    public static DeleteAdminPlaceResult PlaceInUse() =>
        new(DeleteAdminPlaceResultStatus.InUse);
}
