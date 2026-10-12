using DeepResearchAgent.Engine.DTOs;

namespace DeepResearchAgent.Engine.Interfaces;

public interface ICrawler
{
    Task<CrawlResultDto> CrawlAsync(
        string url,
        CancellationToken cancellationToken = default);
}
