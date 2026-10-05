using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchRepository
{
    Task AddFactsAsync(
    IEnumerable<Fact> facts,
    CancellationToken cancellationToken = default);

    Task AddAsync(
        Research research,
        CancellationToken cancellationToken = default);

    Task<Research?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Research?> GetPendingResearchAsync(
        CancellationToken cancellationToken = default);

    Task AddTasksAsync(
        IEnumerable<ResearchTask> tasks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResearchTask>> GetPendingTasksAsync(
        Guid researchId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Research research,
        CancellationToken cancellationToken = default);

    Task AddSourcesAsync(
    IEnumerable<Source> sources,
    CancellationToken cancellationToken = default);
}