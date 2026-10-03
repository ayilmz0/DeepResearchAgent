using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchService
{
    Task<CreateResearchResponse> CreateResearchAsync(
        CreateResearchRequest request,
        CancellationToken cancellationToken = default);

    Task<GetResearchResponse?> GetResearchByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> ProcessPendingResearchAsync(
        CancellationToken cancellationToken = default);
}