namespace DeepResearchAgent.Core.Entities;

public class Report
{
    public Guid Id { get; set; }

    public Guid ResearchId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Research Research { get; set; } = null!;
}