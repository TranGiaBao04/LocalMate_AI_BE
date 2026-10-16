using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class AdminCuratedItineraryService(
    IAdminCuratedItineraryRepository repository,
    ISystemSettingProvider settings) : IAdminCuratedItineraryService
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxCoverImageUrlLength = 500;
    public const int MinPlaces = 2;
    public const int MaxPlaces = 10;
    // Giới hạn nội dung lịch mẫu: tối đa 16 giờ (đặt từ thời chưa hỗ trợ qua nửa đêm, giữ nguyên).
    public const int MaxDurationMinutes = 960;

    public Task<PagedResult<AdminCuratedItineraryResponse>> GetPagedAsync(
        AdminCuratedItineraryQuery query,
        CancellationToken cancellationToken = default) =>
        repository.GetPagedAsync(query, cancellationToken);

    public Task<AdminCuratedItineraryResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        repository.GetByIdAsync(id, cancellationToken);

    public async Task<AdminCuratedItineraryResult> CreateAsync(
        SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken = default)
    {
        var (draft, errors) = await BuildDraftAsync(request, cancellationToken);
        if (draft is null)
        {
            return AdminCuratedItineraryResult.Invalid(errors);
        }

        var id = await repository.AddAsync(draft, cancellationToken);
        var response = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The created curated itinerary could not be read back.");

        return AdminCuratedItineraryResult.Succeeded(response);
    }

    public async Task<AdminCuratedItineraryResult> UpdateAsync(
        Guid id,
        SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken = default)
    {
        var (draft, errors) = await BuildDraftAsync(request, cancellationToken);
        if (draft is null)
        {
            return AdminCuratedItineraryResult.Invalid(errors);
        }

        if (!await repository.UpdateAsync(id, draft, cancellationToken))
        {
            return AdminCuratedItineraryResult.Missing();
        }

        // Lịch bị xoá ngay sau khi sửa xong thì coi như không còn.
        var response = await repository.GetByIdAsync(id, cancellationToken);
        return response is null
            ? AdminCuratedItineraryResult.Missing()
            : AdminCuratedItineraryResult.Succeeded(response);
    }

    public async Task<DeleteAdminCuratedItineraryResultStatus> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await repository.SoftDeleteAsync(id, cancellationToken)
            ? DeleteAdminCuratedItineraryResultStatus.Success
            : DeleteAdminCuratedItineraryResultStatus.NotFound;
    }

    // Trả Draft = null khi có lỗi; mọi lỗi tìm được gom chung theo field.
    private async Task<(CuratedItineraryDraft? Draft, Dictionary<string, string[]> Errors)> BuildDraftAsync(
        SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var title = CollapseWhitespace(request.Title);
        if (title.Length is 0 or > MaxTitleLength)
        {
            errors[nameof(request.Title)] = [$"Tiêu đề bắt buộc, tối đa {MaxTitleLength} ký tự."];
        }

        var description = TrimToNull(request.Description);
        if (description?.Length > MaxDescriptionLength)
        {
            errors[nameof(request.Description)] = [$"Mô tả tối đa {MaxDescriptionLength} ký tự."];
        }

        var coverImageUrl = TrimToNull(request.CoverImageUrl);
        if (coverImageUrl?.Length > MaxCoverImageUrlLength)
        {
            errors[nameof(request.CoverImageUrl)] = [$"Đường dẫn ảnh bìa tối đa {MaxCoverImageUrlLength} ký tự."];
        }

        var placeIds = request.PlaceIds ?? [];
        IReadOnlyList<CuratedPlaceCandidate> places = [];
        var duration = 0;

        // Chỉ đọc DB khi danh sách id đã đúng dạng; chỉ tính thời lượng khi mọi địa điểm đều dùng được.
        string[] placeErrors = GetPlaceIdsError(placeIds) is { } shapeError ? [shapeError] : [];
        if (placeErrors.Length == 0)
        {
            (places, placeErrors) = await LoadPlacesAsync(placeIds, cancellationToken);
        }

        if (placeErrors.Length == 0)
        {
            duration = await EstimateDurationAsync(places, cancellationToken);
            if (duration > MaxDurationMinutes)
            {
                placeErrors =
                [
                    $"Tổng thời lượng ước tính {duration} phút vượt quá {MaxDurationMinutes} phút. Hãy bớt địa điểm."
                ];
            }
        }

        if (placeErrors.Length > 0)
        {
            errors[nameof(request.PlaceIds)] = placeErrors;
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var draft = new CuratedItineraryDraft(
            title,
            description,
            coverImageUrl,
            duration,
            places.Sum(place => place.EstimatedCostMin),
            places.Sum(place => place.EstimatedCostMax),
            placeIds.ToList());

        return (draft, errors);
    }

    private static string? GetPlaceIdsError(IReadOnlyList<Guid> placeIds)
    {
        if (placeIds.Count is < MinPlaces or > MaxPlaces)
        {
            return $"Lịch trình mẫu cần từ {MinPlaces} đến {MaxPlaces} địa điểm.";
        }

        if (placeIds.Contains(Guid.Empty))
        {
            return "Danh sách địa điểm có id không hợp lệ.";
        }

        return placeIds.Distinct().Count() != placeIds.Count
            ? "Mỗi địa điểm chỉ được xuất hiện một lần."
            : null;
    }

    // Trả địa điểm theo đúng thứ tự admin gửi, hoặc các lỗi nếu có địa điểm không dùng được.
    private async Task<(IReadOnlyList<CuratedPlaceCandidate> Places, string[] Errors)> LoadPlacesAsync(
        IReadOnlyList<Guid> placeIds,
        CancellationToken cancellationToken)
    {
        var found = (await repository.GetPlacesAsync(placeIds, cancellationToken))
            .ToDictionary(place => place.Id);
        var errors = new List<string>();

        // Id không có trong DB thì không có tên để hiển thị, chỉ báo số lượng.
        var unknownCount = placeIds.Count(id => !found.ContainsKey(id));
        if (unknownCount > 0)
        {
            errors.Add($"Có {unknownCount} địa điểm không còn tồn tại. Hãy chọn lại.");
        }

        var known = placeIds.Where(found.ContainsKey).Select(id => found[id]).ToList();

        var deleted = known.Where(place => place.IsDeleted).ToList();
        if (deleted.Count > 0)
        {
            errors.Add($"Địa điểm đã bị xoá: {Names(deleted)}.");
        }

        var inactive = known.Where(place => !place.IsDeleted && place.Status != PlaceStatus.Active).ToList();
        if (inactive.Count > 0)
        {
            errors.Add($"Địa điểm chưa ở trạng thái Active: {Names(inactive)}.");
        }

        return errors.Count > 0 ? ([], errors.ToArray()) : (known, []);
    }

    private static string Names(IEnumerable<CuratedPlaceCandidate> places) =>
        string.Join(", ", places.Select(place => place.Name));

    // Cùng hàm với luồng áp dụng lịch mẫu (xuất phát tại chặng đầu, phương tiện Auto) để số phút khớp lúc user áp dụng.
    private async Task<int> EstimateDurationAsync(
        IReadOnlyList<CuratedPlaceCandidate> places,
        CancellationToken cancellationToken)
    {
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        var source = new CuratedItineraryForApplyReadModel(
            Guid.Empty,
            string.Empty,
            0,
            0,
            0,
            places
                .Select((place, index) => new CuratedPlaceForApplyReadModel(
                    place.Id, index, place.Latitude, place.Longitude, place.EstimatedCostMax, place.Category))
                .ToList());

        return CuratedTripBuilder.TotalMinutes(source, TravelMode.Auto, null, planning);
    }

    private static string CollapseWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
