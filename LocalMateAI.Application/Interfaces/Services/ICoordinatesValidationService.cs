namespace LocalMateAI.Application.Interfaces.Services;

public interface ICoordinatesValidationService
{
    bool IsValidHcmcCoordinate(double lat, double lng);
    (bool IsValid, string Reason) ValidateCoordinate(double lat, double lng);
}
