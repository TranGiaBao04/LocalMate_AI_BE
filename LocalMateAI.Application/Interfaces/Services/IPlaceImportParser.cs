using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceImportParser
{
    Task<IReadOnlyList<ParsedPlaceRow>> ParseAsync(Stream stream, string fileExtension, CancellationToken cancellationToken = default);
}
