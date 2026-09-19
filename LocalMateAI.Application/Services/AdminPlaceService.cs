using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Application.Services;

public sealed class AdminPlaceService(
    IPlaceRepository placeRepository,
    ICoordinatesValidationService coordinatesValidationService) : IAdminPlaceService
{
    private const int MaxNameLength = 200;
    private const int MaxAddressLength = 300;
    private const decimal MaxStoredCost = 999_999_999_999m;

    public async Task<AdminPlaceOperationResult> CreateAsync(
        CreateAdminPlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationErrors = Validate(
            request.Name,
            request.Address,
            request.Latitude,
            request.Longitude,
            request.Category,
            request.EstimatedCostMin,
            request.EstimatedCostMax);

        if (validationErrors.Count > 0)
        {
            return AdminPlaceOperationResult.ValidationFailed(validationErrors);
        }

        var place = new Place
        {
            Name = request.Name!.Trim(),
            Description = request.Description,
            Address = request.Address!.Trim(),
            Location = CreatePoint(request.Latitude, request.Longitude),
            Category = request.Category,
            Status = PlaceStatus.Pending,
            EstimatedCostMin = request.EstimatedCostMin,
            EstimatedCostMax = request.EstimatedCostMax,
            ImageUrl = request.ImageUrl
        };

        await placeRepository.AddAsync(place, cancellationToken);
        await placeRepository.SaveChangesAsync(cancellationToken);

        return AdminPlaceOperationResult.Succeeded(Map(place));
    }

    public Task<IReadOnlyList<AdminPlaceResponse>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        placeRepository.GetAllForAdminAsync(cancellationToken);

    public Task<AdminPlaceResponse?> GetByIdAsync(
        Guid placeId,
        CancellationToken cancellationToken = default) =>
        placeRepository.GetAdminByIdAsync(placeId, cancellationToken);

    public async Task<AdminPlaceOperationResult> UpdateAsync(
        Guid placeId,
        UpdateAdminPlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationErrors = Validate(
            request.Name,
            request.Address,
            request.Latitude,
            request.Longitude,
            request.Category,
            request.EstimatedCostMin,
            request.EstimatedCostMax);

        if (validationErrors.Count > 0)
        {
            return AdminPlaceOperationResult.ValidationFailed(validationErrors);
        }

        var place = await placeRepository.GetByIdAsync(placeId, cancellationToken);
        if (place is null)
        {
            return AdminPlaceOperationResult.Missing();
        }

        place.Name = request.Name!.Trim();
        place.Description = request.Description;
        place.Address = request.Address!.Trim();
        place.Location = CreatePoint(request.Latitude, request.Longitude);
        place.Category = request.Category;
        place.EstimatedCostMin = request.EstimatedCostMin;
        place.EstimatedCostMax = request.EstimatedCostMax;
        place.ImageUrl = request.ImageUrl;

        await placeRepository.SaveChangesAsync(cancellationToken);

        return AdminPlaceOperationResult.Succeeded(Map(place));
    }

    public async Task<DeleteAdminPlaceResult> DeleteAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var persistenceResult = await placeRepository.DeleteForAdminAsync(placeId, cancellationToken);

        return persistenceResult switch
        {
            DeletePlacePersistenceResult.Deleted => DeleteAdminPlaceResult.Succeeded(),
            DeletePlacePersistenceResult.NotFound => DeleteAdminPlaceResult.Missing(),
            DeletePlacePersistenceResult.InUse => DeleteAdminPlaceResult.PlaceInUse(),
            _ => throw new InvalidOperationException("Unknown Place delete result.")
        };
    }

    private IReadOnlyDictionary<string, string[]> Validate(
        string? name,
        string? address,
        double latitude,
        double longitude,
        PlaceCategory category,
        decimal estimatedCostMin,
        decimal estimatedCostMax)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors[nameof(CreateAdminPlaceRequest.Name)] = ["Name is required."];
        }
        else if (name.Trim().Length > MaxNameLength)
        {
            errors[nameof(CreateAdminPlaceRequest.Name)] = [$"Name must not exceed {MaxNameLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            errors[nameof(CreateAdminPlaceRequest.Address)] = ["Address is required."];
        }
        else if (address.Trim().Length > MaxAddressLength)
        {
            errors[nameof(CreateAdminPlaceRequest.Address)] = [$"Address must not exceed {MaxAddressLength} characters."];
        }

        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            errors[nameof(CreateAdminPlaceRequest.Latitude)] = ["Coordinates must be finite numbers."];
        }
        else
        {
            var coordinateValidation = coordinatesValidationService.ValidateCoordinate(latitude, longitude);
            if (!coordinateValidation.IsValid)
            {
                errors[nameof(CreateAdminPlaceRequest.Latitude)] = [coordinateValidation.Reason];
            }
        }

        if (!Enum.IsDefined(category))
        {
            errors[nameof(CreateAdminPlaceRequest.Category)] = ["Category is invalid."];
        }

        if (!IsValidStoredCost(estimatedCostMin))
        {
            errors[nameof(CreateAdminPlaceRequest.EstimatedCostMin)] =
                ["EstimatedCostMin must be a non-negative whole number with at most 12 digits."];
        }

        if (!IsValidStoredCost(estimatedCostMax))
        {
            errors[nameof(CreateAdminPlaceRequest.EstimatedCostMax)] =
                ["EstimatedCostMax must be a non-negative whole number with at most 12 digits."];
        }

        if (estimatedCostMin > estimatedCostMax)
        {
            errors[nameof(CreateAdminPlaceRequest.EstimatedCostMax)] =
                ["EstimatedCostMax must be greater than or equal to EstimatedCostMin."];
        }

        return errors;
    }

    private static bool IsValidStoredCost(decimal value) =>
        value >= 0 && value <= MaxStoredCost && decimal.Truncate(value) == value;

    private static Point CreatePoint(double latitude, double longitude) =>
        new(longitude, latitude) { SRID = 4326 };

    private static AdminPlaceResponse Map(Place place) =>
        new(
            place.Id,
            place.Name,
            place.Description,
            place.Address,
            place.Location.Y,
            place.Location.X,
            place.Category.ToString(),
            place.Status.ToString(),
            place.EstimatedCostMin,
            place.EstimatedCostMax,
            place.ImageUrl,
            place.CreatedAt,
            place.UpdatedAt);
}
