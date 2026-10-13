
using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchChunkRetriever
{
    Task<IReadOnlyList<ResearchChunkSearchResultDto>> SearchAsync(
        Guid researchId,
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
