using DeepResearchAgent.Core.Enums;

namespace DeepResearchAgent.Core.Entities;

public class ResearchTask
{
    public Guid Id { get; set; }

    public Guid ResearchId { get; set; }

    public string Query { get; set; } = string.Empty;

    public ResearchStatus Status { get; set; }

    public int Depth { get; set; }

    public Research Research { get; set; } = null!;
}