
namespace DeepResearchAgent.Engine.Interfaces;

public interface IEmbeddingService
{
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string inputType,
        CancellationToken cancellationToken = default);
}
