using LocalMateAI.Application.DTOs.Search;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
