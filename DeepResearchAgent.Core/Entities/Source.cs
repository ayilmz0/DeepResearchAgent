namespace DeepResearchAgent.Core.Entities;

public class Source
{
    public Guid Id { get; set; }

    public Guid ResearchId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Content { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? CrawledAt { get; set; }
    public bool CrawlSucceeded { get; set; }

    public int Depth { get; set; }

    public double RelevanceScore { get; set; }

    public Research Research { get; set; } = null!;

    public ICollection<Fact> Facts { get; set; } = new List<Fact>();
}