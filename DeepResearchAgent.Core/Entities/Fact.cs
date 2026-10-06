using DeepResearchAgent.Core.Enums;

namespace DeepResearchAgent.Core.Entities;

public class Fact
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public string Claim { get; set; } = string.Empty;

    public string? Value { get; set; }

    // Gemini extraction confidence
    public double Confidence { get; set; }

    // Cross-source verification
    public FactVerificationStatus? VerificationStatus { get; set; }

    public double? VerificationConfidence { get; set; }

    public int SupportingSourceCount { get; set; }

    public int ContradictingSourceCount { get; set; }

    public string? VerificationSummary { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public Source Source { get; set; } = null!;

    public ICollection<Citation> Citations { get; set; }
        = new List<Citation>();
}