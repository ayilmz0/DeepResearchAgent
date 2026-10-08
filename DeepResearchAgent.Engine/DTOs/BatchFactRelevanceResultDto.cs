namespace DeepResearchAgent.Engine.DTOs;

public class BatchFactRelevanceResultDto
{
    public Guid FactId { get; set; }

    public bool IsRelevant { get; set; }

    public double Confidence { get; set; }

    public string Reason { get; set; } = string.Empty;
}