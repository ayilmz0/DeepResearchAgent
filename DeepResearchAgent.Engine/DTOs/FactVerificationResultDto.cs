using DeepResearchAgent.Core.Enums;

namespace DeepResearchAgent.Engine.DTOs;

public class FactVerificationResultDto
{
    public FactVerificationStatus Status { get; set; }

    public double VerificationConfidence { get; set; }

    public int SupportingSourceCount { get; set; }

    public int ContradictingSourceCount { get; set; }

    public string Summary { get; set; } = string.Empty;
}