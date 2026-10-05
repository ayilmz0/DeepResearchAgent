using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IFactVerifier
{
    Task<FactVerificationResultDto> VerifyAsync(
        Fact fact,
        IReadOnlyList<Fact> relatedFacts,
        CancellationToken cancellationToken = default);
}