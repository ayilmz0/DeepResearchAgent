
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IFactVerifier
{
    Task<FactVerificationResultDto> VerifyAsync(
        Fact fact,
        IReadOnlyList<Fact> relatedFacts,
        IReadOnlyList<Source> sources,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchFactVerificationResultDto>> VerifyBatchAsync(
        IReadOnlyList<Fact> facts,
        IReadOnlyList<Fact> allFacts,
        IReadOnlyList<Source> sources,
        CancellationToken cancellationToken = default);
}
