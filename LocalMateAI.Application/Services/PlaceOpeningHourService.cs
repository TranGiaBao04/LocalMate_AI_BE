using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class PlaceOpeningHourService(IPlaceOpeningHourRepository repository) : IPlaceOpeningHourService
{
    public async Task<PlaceOpeningHoursResponse?> GetAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        var stored = await repository.GetByPlaceAsync(placeId, cancellationToken);
        return stored is null ? null : PlaceOpeningHourRules.ToResponse(placeId, stored);
    }

    public async Task<SavePlaceOpeningHoursResult> SaveAsync(
        Guid placeId,
        SavePlaceOpeningHoursRequest request,
        CancellationToken cancellationToken = default)
    {
        // Kiểm tra body trước, nên body sai gửi tới id lạ nhận 400 (như các API admin khác).
        var (days, errors) = PlaceOpeningHourRules.Validate(request);
        if (days is null)
        {
            return new SavePlaceOpeningHoursResult(
                SavePlaceOpeningHoursResultStatus.ValidationFailed, ValidationErrors: errors);
        }

        return await repository.ReplaceAsync(placeId, days, cancellationToken)
            ? new SavePlaceOpeningHoursResult(
                SavePlaceOpeningHoursResultStatus.Success, PlaceOpeningHourRules.ToResponse(placeId, days))
            : new SavePlaceOpeningHoursResult(SavePlaceOpeningHoursResultStatus.NotFound);
    }
}
