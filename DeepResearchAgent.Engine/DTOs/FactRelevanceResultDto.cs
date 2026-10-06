namespace DeepResearchAgent.Engine.DTOs;

public class FactRelevanceResultDto
{
    public bool IsRelevant { get; set; }

    public double Confidence { get; set; }

    public string Reason { get; set; } = string.Empty;
}