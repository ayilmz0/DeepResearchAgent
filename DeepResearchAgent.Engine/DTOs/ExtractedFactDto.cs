namespace DeepResearchAgent.Engine.DTOs;

public class ExtractedFactDto
{
    public string Claim { get; set; } = string.Empty;

    public string? Value { get; set; }

    public double Confidence { get; set; }
}