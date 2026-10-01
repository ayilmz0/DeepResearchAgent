using DeepResearchAgent.Core.Enums;

namespace DeepResearchAgent.Core.Entities;

public class Research
{
    public Guid Id { get; set; }

    public string Query { get; set; } = string.Empty;

    public ResearchStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public ICollection<ResearchTask> Tasks { get; set; } = new List<ResearchTask>();

    public ICollection<Source> Sources { get; set; } = new List<Source>();

    public ICollection<Report> Reports { get; set; } = new List<Report>();
}