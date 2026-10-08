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

    Task<IReadOnlyList<ResearchTask>> GetPendingTasksAsync(
        Guid researchId,
        CancellationToken cancellationToken = default);

    Task AddSourcesAsync(
        IEnumerable<Source> sources,
        CancellationToken cancellationToken = default);

    Task AddFactsAsync(
        IEnumerable<Fact> facts,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Fact>> GetFactsByResearchIdAsync(
        Guid researchId,
        CancellationToken cancellationToken = default);

    Task UpdateFactsAsync(
        IEnumerable<Fact> facts,
        CancellationToken cancellationToken = default);

    Task UpdateTaskAsync(
    ResearchTask task,
    CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Research research,
        CancellationToken cancellationToken = default);

    Task<Report?> GetReportByResearchIdAsync(
        Guid researchId,
        CancellationToken cancellationToken = default);

    Task AddReportAsync(
    Report report,
    CancellationToken cancellationToken = default);
}