using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchPlanner
{
    Task<IReadOnlyList<ResearchTask>> CreatePlanAsync(
        Research research,
        CancellationToken cancellationToken = default);
}