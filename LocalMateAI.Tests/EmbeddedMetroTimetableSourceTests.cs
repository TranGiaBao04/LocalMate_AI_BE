using System.Text;
using LocalMateAI.Infrastructure.Metro;

namespace LocalMateAI.Tests;

public sealed class EmbeddedMetroTimetableSourceTests
{
    // Bắt lỗi file metro-timetable.json thật ngay lúc dotnet test, trước khi deploy.
    [Fact]
    public void Load_ParsesRealEmbeddedTimetable()
    {
        var timetable = EmbeddedMetroTimetableSource.Load().Timetable;

        Assert.Equal(14, timetable.StationOffsets.Count);
        Assert.NotEmpty(timetable.Services);
    }

    [Fact]
    public void Parse_RejectsTimeNotInHourMinuteFormat()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Parse(ValidJson().Replace("\"05:00\"", "\"5:00\"")));

        Assert.Contains("HH:mm", exception.Message);
    }

    [Fact]
    public void Parse_RejectsTimetableBreakingValidationRules()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Parse(ValidJson().Replace("\"effectiveTo\": null", "\"effectiveTo\": \"2020-01-01\"")));

        Assert.Contains("effectiveTo", exception.Message);
    }

    private static void Parse(string json) =>
        EmbeddedMetroTimetableSource.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    // Dùng lại chính file nhúng làm JSON hợp lệ rồi sửa từng chỗ.
    private static string ValidJson()
    {
        using var stream = typeof(EmbeddedMetroTimetableSource).Assembly
            .GetManifestResourceStream("MetroTimetable.metro-timetable.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
