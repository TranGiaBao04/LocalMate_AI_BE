using LocalMateAI.Application.DTOs.MasterData;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IMasterDataService
{
    Task<MasterDataResponse> GetMasterDataAsync(CancellationToken cancellationToken = default);
}
