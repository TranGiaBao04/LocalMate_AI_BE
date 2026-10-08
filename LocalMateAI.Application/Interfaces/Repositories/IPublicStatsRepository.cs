namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPublicStatsRepository
{
    /// <summary>Tổng số trip đã chốt từ trước tới nay, kể cả trip đã xoá mềm.</summary>
    Task<long> CountFinalizedTripsAsync(CancellationToken cancellationToken = default);
}
