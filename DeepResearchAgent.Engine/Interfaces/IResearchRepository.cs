using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchRepository
{
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

    Task UpdateAsync(
        Research research,
        CancellationToken cancellationToken = default);
}