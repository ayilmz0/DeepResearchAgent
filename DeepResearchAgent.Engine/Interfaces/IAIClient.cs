namespace DeepResearchAgent.Engine.Interfaces;

public interface IAIClient
{
    Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}