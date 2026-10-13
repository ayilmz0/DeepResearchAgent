
using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface IResearchChunkIndexer
{
    Task IndexSourcesAsync(
        IReadOnlyCollection<Source> sources,
        CancellationToken cancellationToken = default);
}
