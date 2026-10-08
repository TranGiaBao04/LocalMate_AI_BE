using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlaceEmbeddingRepository(AppDbContext context) : IPlaceEmbeddingRepository
{
    public async Task<IReadOnlyList<PlaceEmbeddingSource>> GetSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        return await context.Places
            .AsNoTracking()
            .Where(place => place.Status == PlaceStatus.Active && place.DeletedAt == null)
            .Select(place => new PlaceEmbeddingSource(
                place.Id,
                place.Name,
                place.Category,
                place.Description,
                place.Tags
                    .Where(placeTag => placeTag.Tag.IsActive)
                    .Select(placeTag => placeTag.Tag.Name)
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlaceEmbeddingState>> GetStatesAsync(
        CancellationToken cancellationToken = default)
    {
        return await context.PlaceEmbeddings
            .AsNoTracking()
            .Select(embedding => new PlaceEmbeddingState(embedding.PlaceId, embedding.Model, embedding.ContentHash))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlaceEmbeddingVector>> GetVectorsAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        return await context.PlaceEmbeddings
            .AsNoTracking()
            .Where(embedding => embedding.Model == model)
            .Select(embedding => new PlaceEmbeddingVector(embedding.PlaceId, embedding.Vector))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> UpsertAsync(
        IReadOnlyList<PlaceEmbeddingEntry> entries,
        DateTime updatedAt,
        CancellationToken cancellationToken = default)
    {
        var written = 0;

        foreach (var entry in entries)
        {
            // Chỉ ghi khi địa điểm vẫn đang hiển thị: địa điểm bị ẩn/xoá trong lúc chờ nhà cung cấp thì bỏ qua.
            written += await context.Database.ExecuteSqlAsync($"""
                INSERT INTO "PlaceEmbeddings" ("PlaceId", "Model", "ContentHash", "Vector", "UpdatedAt")
                SELECT p."Id", {entry.Model}, {entry.ContentHash}, {entry.Vector}, {updatedAt}
                FROM "Places" p
                WHERE p."Id" = {entry.PlaceId} AND p."Status" = 'Active' AND p."DeletedAt" IS NULL
                ON CONFLICT ("PlaceId") DO UPDATE
                SET "Model" = EXCLUDED."Model",
                    "ContentHash" = EXCLUDED."ContentHash",
                    "Vector" = EXCLUDED."Vector",
                    "UpdatedAt" = EXCLUDED."UpdatedAt"
                """, cancellationToken);
        }

        return written;
    }

    public async Task<int> DeleteForInactivePlacesAsync(CancellationToken cancellationToken = default)
    {
        return await context.PlaceEmbeddings
            .Where(embedding => !context.Places.Any(place =>
                place.Id == embedding.PlaceId
                && place.Status == PlaceStatus.Active
                && place.DeletedAt == null))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
