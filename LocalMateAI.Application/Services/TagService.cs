using LocalMateAI.Application.DTOs.Tags;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TagService(ITagRepository tagRepository) : ITagService
{
    public async Task<IReadOnlyList<TagResponse>> GetActiveTagsAsync(
        CancellationToken cancellationToken = default)
    {
        var tags = await tagRepository.GetActiveAsync(cancellationToken);

        return tags
            .Select(tag => new TagResponse(
                tag.Id,
                tag.Name,
                tag.Type.ToString()))
            .ToArray();
    }
}
