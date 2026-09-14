using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TagRepository(AppDbContext dbContext) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> GetByIdsAsync(
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken = default) =>
        await dbContext.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Tag>> GetActiveAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Tags
            .AsNoTracking()
            .Where(tag => tag.IsActive)
            .OrderBy(tag => tag.Type)
            .ThenBy(tag => tag.Name)
            .ToListAsync(cancellationToken);
}
