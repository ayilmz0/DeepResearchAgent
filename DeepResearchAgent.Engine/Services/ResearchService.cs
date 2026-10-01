using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Engine.Services;

public class ResearchService : IResearchService
{
    public Task<CreateResearchResponse> CreateResearchAsync(
        CreateResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var research = new Research
        {
            Id = Guid.NewGuid(),
            Query = request.Query,
            Status = ResearchStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        var response = new CreateResearchResponse
        {
            Id = research.Id
        };

        return Task.FromResult(response);
    }
}