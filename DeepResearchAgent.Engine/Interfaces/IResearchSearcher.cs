using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchSearcher
{
    Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}