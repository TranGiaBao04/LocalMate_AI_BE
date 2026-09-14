using LocalMateAI.Application.DTOs.Tags;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITagService
{
    Task<IReadOnlyList<TagResponse>> GetActiveTagsAsync(
        CancellationToken cancellationToken = default);
}
