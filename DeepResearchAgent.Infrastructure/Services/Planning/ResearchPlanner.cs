using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Planning;

public class ResearchPlanner : IResearchPlanner
{
    public Task<IReadOnlyList<ResearchTask>> CreatePlanAsync(
        Research research,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = research.Query.Trim();

        var queries = new[]
        {
            query,
            $"{query} benefits and real-world applications",
            $"{query} challenges and future trends"
        };

        var tasks = queries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(taskQuery => new ResearchTask
            {
                Id = Guid.NewGuid(),
                ResearchId = research.Id,
                Query = taskQuery,
                Status = ResearchStatus.Pending,
                Depth = 0
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ResearchTask>>(tasks);
    }
}
