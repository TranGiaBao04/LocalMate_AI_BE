using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TrialProgramRepository(AppDbContext dbContext) : ITrialProgramRepository
{
    public async Task<IReadOnlyList<TrialProgramReadModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await ToReadModels(dbContext.TrialPrograms
                .AsNoTracking()
                .OrderByDescending(program => program.CreatedAt)
                .ThenBy(program => program.Id))
            .ToListAsync(cancellationToken);

    public Task<TrialProgramReadModel?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToReadModels(dbContext.TrialPrograms.AsNoTracking().Where(program => program.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<TrialProgramReadModel?> GetEnabledForDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        ToReadModels(dbContext.TrialPrograms
                .AsNoTracking()
                .Where(program => program.IsEnabled && program.StartsOn <= date && date <= program.EndsOn)
                .OrderBy(program => program.StartsOn)
                .ThenBy(program => program.Id))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasEnabledOverlapAsync(DateOnly startsOn, DateOnly endsOn, Guid? exceptId,
        CancellationToken cancellationToken = default) =>
        dbContext.TrialPrograms.AnyAsync(
            program => program.IsEnabled
                && (exceptId == null || program.Id != exceptId)
                && program.StartsOn <= endsOn
                && startsOn <= program.EndsOn,
            cancellationToken);

    public async Task AddAsync(TrialProgram program, CancellationToken cancellationToken = default)
    {
        dbContext.TrialPrograms.Add(program);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Guid id, TrialProgramDraft draft, CancellationToken cancellationToken = default)
    {
        var program = await dbContext.TrialPrograms.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (program is null)
        {
            return false;
        }

        program.Name = draft.Name;
        program.StartsOn = draft.StartsOn;
        program.EndsOn = draft.EndsOn;
        program.TrialDays = draft.TrialDays;
        program.MaxRecipients = draft.MaxRecipients;
        program.IsEnabled = draft.IsEnabled;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Sắp xếp phải đặt TRƯỚC khi chiếu sang record (EF không dịch được OrderBy trên record tạo bằng constructor).
    private IQueryable<TrialProgramReadModel> ToReadModels(IQueryable<TrialProgram> programs) =>
        programs.Select(program => new TrialProgramReadModel(
            program.Id,
            program.Name,
            program.StartsOn,
            program.EndsOn,
            program.TrialDays,
            program.MaxRecipients,
            program.IsEnabled,
            dbContext.PlanGrants.Count(grant => grant.TrialProgramId == program.Id),
            program.CreatedAt,
            program.UpdatedAt));
}
