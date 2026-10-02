namespace LocalMateAI.Application.Security;

/// <summary>
/// BE-58: Policy thuần quyết định request mutation lên /api/trips/{tripId}/...
/// có cần guard trạng thái Finalized hay không.
/// Exempt: "finalize" (chính transition), "fork" (BE-59 — nhân bản thành draft mới)
/// và DELETE /api/trips/{id} (xoá mềm, được phép cả với trip đã chốt).
/// Lưu ý: mọi endpoint items/... của trip Finalized hợp lệ sau này (nếu có) phải thêm exempt.
/// </summary>
public static class TripActionGuardPolicy
{
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    private static readonly HashSet<string> ExemptSuffixes =
        new(StringComparer.OrdinalIgnoreCase) { "finalize", "fork" };

    /// <summary>Trả true nếu request cần guard; kèm tripId parse được từ path.</summary>
    public static bool ShouldGuard(string httpMethod, string path, out Guid? tripId)
    {
        tripId = null;
        if (string.IsNullOrWhiteSpace(path) || !MutatingMethods.Contains(httpMethod))
        {
            return false;
        }

        var segments = path.Trim('/').Split('/'); // [api, trips, {id}, ...]
        if (segments.Length < 3
            || !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "trips", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[2], out var parsedTripId))
        {
            return false;
        }

        tripId = parsedTripId;

        // DELETE /api/trips/{id} (đúng 3 đoạn) là xoá mềm nên không bị khoá bởi trạng thái Finalized.
        if (string.Equals(httpMethod, "DELETE", StringComparison.OrdinalIgnoreCase) && segments.Length == 3)
        {
            return false;
        }

        var suffix = segments.Length > 3 ? segments[3] : string.Empty;
        return !ExemptSuffixes.Contains(suffix); // suffix rỗng → guard (mọi mutation trực tiếp lên trip)
    }
}