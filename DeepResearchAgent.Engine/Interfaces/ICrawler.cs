using DeepResearchAgent.Core.Entities;

namespace DeepResearchAgent.Engine.Interfaces;

public interface ICrawler
{
    Task<string> CrawlAsync(
        string url,
        CancellationToken cancellationToken = default);
}