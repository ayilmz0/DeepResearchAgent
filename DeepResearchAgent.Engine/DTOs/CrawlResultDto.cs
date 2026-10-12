namespace DeepResearchAgent.Engine.DTOs;

public class CrawlResultDto
{
    public string Content { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? CanonicalUrl { get; set; }

    public string? Doi { get; set; }
}
