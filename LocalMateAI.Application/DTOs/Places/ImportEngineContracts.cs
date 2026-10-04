using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Places;

public enum PlaceImportCommitMode
{
    ValidOnly = 0,
    AbortOnError = 1
}

public sealed record PlaceImportPreviewRowResult(
    int RowNumber,
    bool IsValid,
    bool IsDuplicateCandidate,
    string RawName,
    string RawAddress,
    string RawCategory,
    PlaceCategory? NormalizedCategory,
    double? Latitude,
    double? Longitude,
    int? EstimatedCostMin,
    int? EstimatedCostMax,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<string> MatchedStationNames,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string? RawCoordinates,
    string? RawPriceMin,
    string? RawPriceMax,
    string? RawStations,
    string? RawOpenHours,
    string? RawTags,
    string? RawDescription
);

public sealed record PlaceImportPreviewResponse(
    string ImportId,
    int TotalRows,
    int ValidRowsCount,
    int ErrorRowsCount,
    int WarningRowsCount,
    IReadOnlyList<PlaceImportPreviewRowResult> Rows
);

public sealed record CommitPlaceImportRequest(
    string ImportId,
    PlaceImportCommitMode Mode = PlaceImportCommitMode.ValidOnly
);

public sealed record PlaceImportCommitResponse(
    string ImportId,
    int CommittedCount,
    int SkippedCount,
    int FailedCount,
    DateTime CommittedAt
);

public enum PlaceImportCommitResultStatus
{
    Success,
    SessionNotFoundOrExpired,
    AlreadyCommittedOrInProgress,
    AbortedDueToErrors,
    TransactionFailed
}

public sealed record PlaceImportCommitResult(
    PlaceImportCommitResultStatus Status,
    PlaceImportCommitResponse? Response = null,
    string? ErrorMessage = null
);
