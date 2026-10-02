using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Infrastructure.Metro;

public sealed class EmbeddedMetroTimetableSource : IMetroTimetableSource
{
    // Khớp LogicalName khai báo trong LocalMateAI.Infrastructure.csproj.
    private const string ResourceName = "MetroTimetable.metro-timetable.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new HourMinuteTimeOnlyConverter() }
    };

    private EmbeddedMetroTimetableSource(MetroTimetable timetable) => Timetable = timetable;

    public MetroTimetable Timetable { get; }

    /// <summary>Đọc và kiểm tra ngay khi gọi; file sai thì ném lỗi để app không khởi động được.</summary>
    public static EmbeddedMetroTimetableSource Load()
    {
        using var stream = typeof(EmbeddedMetroTimetableSource).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Metro timetable resource '{ResourceName}' was not found.");

        return new EmbeddedMetroTimetableSource(Parse(stream));
    }

    public static MetroTimetable Parse(Stream json)
    {
        MetroTimetable? timetable;
        try
        {
            timetable = JsonSerializer.Deserialize<MetroTimetable>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Metro timetable JSON is malformed: {exception.Message}", exception);
        }

        if (timetable is null)
        {
            throw new InvalidOperationException("Metro timetable JSON is empty.");
        }

        var errors = MetroTimetableValidator.Validate(timetable);
        return errors.Count == 0
            ? timetable
            : throw new InvalidOperationException(
                "Metro timetable is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}
