namespace DeepResearchAgent.Engine.DTOs;

public class GetResearchResponse
{
    public Guid Id { get; set; }

    public string Query { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}