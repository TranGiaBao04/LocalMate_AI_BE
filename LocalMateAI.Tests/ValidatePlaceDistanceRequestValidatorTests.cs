using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Places;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class ValidatePlaceDistanceRequestValidatorTests
{
    private readonly ValidatePlaceDistanceRequestValidator _validator =
        new(new CoordinatesValidationService());

    [Fact]
    public async Task Validate_ValidHcmcCoordinates_ReturnsNoErrors()
    {
        var request = new ValidatePlaceDistanceRequest(10.7769, 106.7009);

        var result = await _validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_InvalidHcmcCoordinates_ReturnsValidationError()
    {
        var request = new ValidatePlaceDistanceRequest(21.0285, 105.8542);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("nằm ngoài phạm vi TP.HCM"));
    }

    [Fact]
    public async Task Validate_NaNCoordinates_ReturnsValidationError()
    {
        var request = new ValidatePlaceDistanceRequest(double.NaN, 106.7009);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
    }
}
