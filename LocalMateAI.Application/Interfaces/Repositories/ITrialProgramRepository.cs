using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ITrialProgramRepository
{
    // Mới nhất trước.
    Task<IReadOnlyList<TrialProgramReadModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<TrialProgramReadModel?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // Có chương trình ĐANG BẬT nào khác trùng khoảng ngày này không (tính cả hai đầu).
    Task<bool> HasEnabledOverlapAsync(DateOnly startsOn, DateOnly endsOn, Guid? exceptId,
        CancellationToken cancellationToken = default);

    Task AddAsync(TrialProgram program, CancellationToken cancellationToken = default);

    // false = không có chương trình này.
    Task<bool> UpdateAsync(Guid id, TrialProgramDraft draft, CancellationToken cancellationToken = default);
}
