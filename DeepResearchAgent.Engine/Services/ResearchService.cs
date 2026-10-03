using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Engine.Services;

public class ResearchService : IResearchService
{
    private readonly IResearchRepository _researchRepository;

    public ResearchService(IResearchRepository researchRepository)
    {
        _researchRepository = researchRepository;
    }

    public async Task<bool> ProcessPendingResearchAsync(
    CancellationToken cancellationToken = default)
    {
        var research = await _researchRepository.GetPendingResearchAsync(
            cancellationToken);

        if (research is null)
        {
            return false;
        }

        research.Status = ResearchStatus.Planning;
        research.StartedAt = DateTime.UtcNow;

        await _researchRepository.UpdateAsync(
            research,
            cancellationToken);

        return true;
    }

    public async Task<CreateResearchResponse> CreateResearchAsync(
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

        await _researchRepository.AddAsync(
            research,
            cancellationToken);

        return new CreateResearchResponse
        {
            Id = research.Id
        };
    }
        
    public async Task<GetResearchResponse?> GetResearchByIdAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        var research = await _researchRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (research is null)
        {
            return null;
        }

        return new GetResearchResponse
        {
            Id = research.Id,
            Query = research.Query,
            Status = research.Status.ToString(),
            CreatedAt = research.CreatedAt,
            StartedAt = research.StartedAt,
            CompletedAt = research.CompletedAt
        };
    }
}