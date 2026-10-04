namespace LocalMateAI.Application.DTOs.Places;

public sealed class PlaceImportTemplateRow
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public decimal? EstimatedCostMin { get; set; }
    public decimal? EstimatedCostMax { get; set; }
    public string? MetroStationName { get; set; }
    public string? Description { get; set; }
    public string? OpeningHours { get; set; }
}

public sealed record PlaceImportTemplateFile(
    byte[] Content,
    string ContentType,
    string FileName);
