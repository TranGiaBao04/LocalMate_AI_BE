using System.Text;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Infrastructure.Services;

public sealed class PlaceImportEngineService(
    IPlaceImportParser parser,
    ICategoryValidationService categoryValidationService,
    ICoordinateNormalizer coordinateNormalizer,
    IPriceNormalizer priceNormalizer,
    IStationMapperService stationMapperService,
    IPlaceRepository placeRepository,
    IMemoryCache cache,
    AppDbContext? dbContext = null) : IPlaceImportEngineService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    public async Task<PlaceImportPreviewResponse> PreviewAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        var ext = Path.GetExtension(fileName);
        var parsedRows = await parser.ParseAsync(fileStream, ext, cancellationToken);

        var importId = "imp_" + Guid.NewGuid().ToString("N");
        var previewRows = new List<PlaceImportPreviewRowResult>();

        int validCount = 0;
        int errorCount = 0;
        int warningCount = 0;

        foreach (var raw in parsedRows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // Required Name check
            if (string.IsNullOrWhiteSpace(raw.RawName))
            {
                errors.Add("Tên địa điểm không được để trống.");
            }

            // Required Address check
            if (string.IsNullOrWhiteSpace(raw.RawAddress))
            {
                errors.Add("Địa chỉ không được để trống.");
            }

            // Category validation
            var catRes = categoryValidationService.Validate(raw.RawCategory);
            if (!catRes.IsValid)
            {
                errors.Add(catRes.ErrorMessage ?? "Danh mục không hợp lệ.");
            }

            // Coordinate normalization & validation
            var coordRes = coordinateNormalizer.Normalize(raw.RawCoordinates, raw.RawLatitude, raw.RawLongitude);
            if (!coordRes.IsValid)
            {
                errors.Add(coordRes.ErrorMessage ?? "Tọa độ không hợp lệ.");
            }

            // Price normalization & validation
            var priceRes = priceNormalizer.Normalize(raw.RawPriceMin, raw.RawPriceMax);
            if (!priceRes.IsValid)
            {
                errors.Add(priceRes.ErrorMessage ?? "Giá tiền không hợp lệ.");
            }

            // Station mapping
            var stationRes = await stationMapperService.MapStationsAsync(raw.RawStations, cancellationToken);
            if (!stationRes.IsValid)
            {
                errors.Add(stationRes.ErrorMessage ?? "Ga Metro không hợp lệ.");
            }

            // Duplicate detection check (<50m and similar name)
            bool isDuplicateCandidate = false;
            if (coordRes.IsValid && coordRes.Latitude.HasValue && coordRes.Longitude.HasValue && !string.IsNullOrWhiteSpace(raw.RawName))
            {
                var nearbyCandidates = await placeRepository.FindNearbyPlacesAsync(
                    coordRes.Latitude.Value,
                    coordRes.Longitude.Value,
                    radiusMeters: 50,
                    cancellationToken);

                foreach (var candidate in nearbyCandidates)
                {
                    if (PlaceDuplicateMatcher.IsSimilarName(candidate.Name, raw.RawName))
                    {
                        isDuplicateCandidate = true;
                        warnings.Add($"Nghi trùng với địa điểm '{candidate.Name}' trong hệ thống (khoảng cách {candidate.DistanceMeters:F1}m).");
                        break;
                    }
                }
            }

            bool isValid = errors.Count == 0;
            if (isValid) validCount++; else errorCount++;
            if (warnings.Count > 0) warningCount++;

            previewRows.Add(new PlaceImportPreviewRowResult(
                RowNumber: raw.RowNumber,
                IsValid: isValid,
                IsDuplicateCandidate: isDuplicateCandidate,
                RawName: raw.RawName,
                RawAddress: raw.RawAddress,
                RawCategory: raw.RawCategory,
                NormalizedCategory: catRes.Category,
                Latitude: coordRes.Latitude,
                Longitude: coordRes.Longitude,
                EstimatedCostMin: priceRes.EstimatedCostMin,
                EstimatedCostMax: priceRes.EstimatedCostMax,
                StationIds: stationRes.StationIds,
                MatchedStationNames: stationRes.MatchedStationNames,
                Errors: errors,
                Warnings: warnings,
                RawCoordinates: raw.RawCoordinates,
                RawPriceMin: raw.RawPriceMin,
                RawPriceMax: raw.RawPriceMax,
                RawStations: raw.RawStations,
                RawOpenHours: raw.RawOpenHours,
                RawTags: raw.RawTags,
                RawDescription: raw.RawDescription
            ));
        }

        var response = new PlaceImportPreviewResponse(
            ImportId: importId,
            TotalRows: parsedRows.Count,
            ValidRowsCount: validCount,
            ErrorRowsCount: errorCount,
            WarningRowsCount: warningCount,
            Rows: previewRows
        );

        // Store in cache for 30 minutes
        cache.Set(GetSessionCacheKey(importId), response, SessionLifetime);

        return response;
    }

    public async Task<PlaceImportCommitResult> CommitAsync(
        CommitPlaceImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionKey = GetSessionCacheKey(request.ImportId);
        if (!cache.TryGetValue<PlaceImportPreviewResponse>(sessionKey, out var preview) || preview is null)
        {
            return new PlaceImportCommitResult(
                PlaceImportCommitResultStatus.SessionNotFoundOrExpired,
                ErrorMessage: $"Phiên import '{request.ImportId}' không tồn tại hoặc đã hết hạn (30 phút). Vui lòng thực hiện preview lại.");
        }

        // Idempotency & Rate Limiting Guard (BE-111)
        var lockKey = GetLockCacheKey(request.ImportId);
        if (cache.TryGetValue(lockKey, out _))
        {
            return new PlaceImportCommitResult(
                PlaceImportCommitResultStatus.AlreadyCommittedOrInProgress,
                ErrorMessage: $"Yêu cầu import '{request.ImportId}' đang được xử lý hoặc đã được commit trước đó.");
        }

        // Set short-term lock key to prevent concurrent submit
        cache.Set(lockKey, true, TimeSpan.FromHours(1));

        // AbortOnError check
        if (request.Mode == PlaceImportCommitMode.AbortOnError && preview.ErrorRowsCount > 0)
        {
            cache.Remove(lockKey); // Release lock if aborted
            return new PlaceImportCommitResult(
                PlaceImportCommitResultStatus.AbortedDueToErrors,
                ErrorMessage: $"Import bị hủy do lựa chọn 'Hủy toàn bộ nếu có lỗi' và phát hiện {preview.ErrorRowsCount} dòng dữ liệu bị lỗi.");
        }

        var rowsToCommit = preview.Rows.Where(r => r.IsValid).ToList();
        if (rowsToCommit.Count == 0)
        {
            cache.Remove(lockKey);
            return new PlaceImportCommitResult(
                PlaceImportCommitResultStatus.Success,
                new PlaceImportCommitResponse(request.ImportId, 0, preview.TotalRows, preview.ErrorRowsCount, DateTime.UtcNow));
        }

        var tx = dbContext?.Database != null ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            int committedCount = 0;
            foreach (var row in rowsToCommit)
            {
                var place = new Place
                {
                    Name = row.RawName.Trim(),
                    Address = row.RawAddress.Trim(),
                    Category = row.NormalizedCategory!.Value,
                    Location = new Point(row.Longitude!.Value, row.Latitude!.Value) { SRID = 4326 },
                    EstimatedCostMin = row.EstimatedCostMin.HasValue ? (decimal)row.EstimatedCostMin.Value : 0m,
                    EstimatedCostMax = row.EstimatedCostMax.HasValue ? (decimal)row.EstimatedCostMax.Value : 0m,
                    Description = string.IsNullOrWhiteSpace(row.RawDescription) ? null : row.RawDescription.Trim(),
                    Status = PlaceStatus.Active,
                    IsVerified = false
                };

                await placeRepository.AddAsync(place, cancellationToken);
                committedCount++;
            }

            await placeRepository.SaveChangesAsync(cancellationToken);
            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
                await tx.DisposeAsync();
            }

            // Evict preview session from cache
            cache.Remove(sessionKey);

            int skippedCount = preview.TotalRows - committedCount;
            return new PlaceImportCommitResult(
                PlaceImportCommitResultStatus.Success,
                new PlaceImportCommitResponse(
                    ImportId: request.ImportId,
                    CommittedCount: committedCount,
                    SkippedCount: skippedCount,
                    FailedCount: preview.ErrorRowsCount,
                    CommittedAt: DateTime.UtcNow
                ));
        }
        catch (Exception ex)
        {
            if (tx != null)
            {
                await tx.RollbackAsync(cancellationToken);
                await tx.DisposeAsync();
            }
            cache.Remove(lockKey);
            return new PlaceImportCommitResult(
                PlaceImportCommitResultStatus.TransactionFailed,
                ErrorMessage: $"Lỗi hệ thống khi commit vào Database: {ex.Message}");
        }
    }

    public byte[]? ExportErrorReportCsv(string importId)
    {
        var sessionKey = GetSessionCacheKey(importId);
        if (!cache.TryGetValue<PlaceImportPreviewResponse>(sessionKey, out var preview) || preview is null)
        {
            return null;
        }

        var errorRows = preview.Rows.Where(r => !r.IsValid).ToList();
        if (errorRows.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        // Add UTF-8 BOM for Excel compatibility
        sb.Append('\uFEFF');
        sb.AppendLine("Dòng,Tên địa điểm,Địa chỉ,Danh mục,Tọa độ,Giá từ,Giá đến,Ga Metro,Lý do lỗi");

        foreach (var r in errorRows)
        {
            var reasons = string.Join(" | ", r.Errors);
            sb.AppendLine($"\"{r.RowNumber}\",\"{EscapeCsv(r.RawName)}\",\"{EscapeCsv(r.RawAddress)}\",\"{EscapeCsv(r.RawCategory)}\",\"{EscapeCsv(r.RawCoordinates)}\",\"{EscapeCsv(r.RawPriceMin)}\",\"{EscapeCsv(r.RawPriceMax)}\",\"{EscapeCsv(r.RawStations)}\",\"{EscapeCsv(reasons)}\"");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string GetSessionCacheKey(string importId) => $"import_session_{importId}";
    private static string GetLockCacheKey(string importId) => $"import_lock_{importId}";
    private static string EscapeCsv(string? val) => (val ?? string.Empty).Replace("\"", "\"\"");
}
