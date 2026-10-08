using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IReportGenerator
{
    Task<Report> GenerateAsync(
        Research research,
        IReadOnlyList<Fact> facts,
        CancellationToken cancellationToken = default);
}