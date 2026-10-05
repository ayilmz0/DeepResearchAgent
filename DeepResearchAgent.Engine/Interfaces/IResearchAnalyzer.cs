using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchAnalyzer
{
    Task<IReadOnlyList<Fact>> AnalyzeAsync(
        Source source,
        CancellationToken cancellationToken = default);
}