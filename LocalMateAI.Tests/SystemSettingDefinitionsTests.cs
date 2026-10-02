using LocalMateAI.Application.Settings;

namespace LocalMateAI.Tests;

public sealed class SystemSettingDefinitionsTests
{
    [Fact]
    public void Definitions_AreUniqueAndDefaultsAreValid()
    {
        var keys = SystemSettingDefinitions.All.Select(definition => definition.Key.ToUpperInvariant()).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());

        Assert.All(SystemSettingDefinitions.All, definition =>
        {
            Assert.True(definition.MinValue <= definition.MaxValue);
            Assert.Null(SystemSettingDefinitions.Validate(definition, definition.DefaultValue));
            Assert.True(definition.Key.Length <= 100);
        });
    }

    [Fact]
    public void Find_IgnoresCaseAndSurroundingSpaces()
    {
        var definition = SystemSettingDefinitions.Find("  stations.minactiveplacesperstation ");

        Assert.Equal(SystemSettingKeys.MinActivePlacesPerStation, definition?.Key);
        Assert.Null(SystemSettingDefinitions.Find("Stations.Unknown"));
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("5.5", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void TryParse_AcceptsOnlyWholeNumbersInRange(string stored, bool expected)
    {
        var definition = SystemSettingDefinitions.Find(SystemSettingKeys.MinActivePlacesPerStation)!;

        Assert.Equal(expected, SystemSettingDefinitions.TryParse(definition, stored, out _));
    }

    [Fact]
    public void Format_WritesIntegerWithoutDecimals()
    {
        var definition = SystemSettingDefinitions.Find(SystemSettingKeys.MinActivePlacesPerStation)!;

        Assert.Equal("8", SystemSettingDefinitions.Format(definition, 8.0m));
    }
}
