namespace DeepResearchAgent.Engine.Interfaces;

public interface IAIClient
{
    Task<string> GenerateAsync(
       string prompt,
       object? responseSchema = null,
       CancellationToken cancellationToken = default);
}