using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceImportEngineService
{
    Task<PlaceImportPreviewResponse> PreviewAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<PlaceImportCommitResult> CommitAsync(
        CommitPlaceImportRequest request,
        CancellationToken cancellationToken = default);

    byte[]? ExportErrorReportCsv(string importId);
}
