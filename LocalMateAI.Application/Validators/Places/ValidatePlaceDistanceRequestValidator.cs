using FluentValidation;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Validators.Places;

public sealed class ValidatePlaceDistanceRequestValidator : AbstractValidator<ValidatePlaceDistanceRequest>
{
    public ValidatePlaceDistanceRequestValidator(ICoordinatesValidationService coordinatesValidationService)
    {
        RuleFor(x => x.Latitude)
            .Must(lat => double.IsFinite(lat))
            .WithMessage("Vĩ độ phải là số thực hợp lệ.");

        RuleFor(x => x.Longitude)
            .Must(lng => double.IsFinite(lng))
            .WithMessage("Kinh độ phải là số thực hợp lệ.");

        RuleFor(x => x)
            .Must(req =>
            {
                if (!double.IsFinite(req.Latitude) || !double.IsFinite(req.Longitude))
                    return true;

                return coordinatesValidationService.IsValidHcmcCoordinate(req.Latitude, req.Longitude);
            })
            .WithMessage("Tọa độ nằm ngoài phạm vi TP.HCM hợp lệ.");
    }
}
