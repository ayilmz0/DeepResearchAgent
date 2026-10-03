using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Planning;

public class ResearchPlanner : IResearchPlanner
{
    public Task<IReadOnlyList<ResearchTask>> CreatePlanAsync(
        Research research,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<ResearchTask>
        {
            new ResearchTask
            {
                Id = Guid.NewGuid(),
                ResearchId = research.Id,
                Query = $"{research.Query} textile industry impact",
                Status = Core.Enums.ResearchStatus.Pending,
                Depth = 0
            },
            new ResearchTask
            {
                Id = Guid.NewGuid(),
                ResearchId = research.Id,
                Query = $"{research.Query} economic impact",
                Status = Core.Enums.ResearchStatus.Pending,
                Depth = 0
            },
            new ResearchTask
            {
                Id = Guid.NewGuid(),
                ResearchId = research.Id,
                Query = $"{research.Query} technology trends",
                Status = Core.Enums.ResearchStatus.Pending,
                Depth = 0
            }
        };

        return Task.FromResult<IReadOnlyList<ResearchTask>>(tasks);
    }
}