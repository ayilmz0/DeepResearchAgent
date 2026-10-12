    using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface ISourceRelevanceAnalyzer
{
    Task<IReadOnlyList<SourceRelevanceResultDto>> AnalyzeBatchAsync(
        Research research,
        IReadOnlyList<Source> sources,
        CancellationToken cancellationToken = default);
}
