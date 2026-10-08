using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IFactRelevanceAnalyzer
{
    Task<FactRelevanceResultDto> AnalyzeAsync(
        Research research,
        Fact fact,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchFactRelevanceResultDto>> AnalyzeBatchAsync(
        Research research,
        IReadOnlyList<Fact> facts,
        CancellationToken cancellationToken = default);
}