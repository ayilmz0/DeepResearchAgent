namespace DeepResearchAgent.Core.Entities;

public class Fact
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public string Claim { get; set; } = string.Empty;

    public string? Value { get; set; }

    // Gemini'nin extraction confidence değeri
    public double Confidence { get; set; }

    // Diğer kaynaklarla doğrulandıktan sonraki güven skoru
    public double? VerificationConfidence { get; set; }

    // Fact'i destekleyen kaynak sayısı
    public int SupportingSourceCount { get; set; }

    // Fact ile çelişen kaynak sayısı
    public int ContradictingSourceCount { get; set; }

    public string? VerificationSummary { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public Source Source { get; set; } = null!;

    public ICollection<Citation> Citations { get; set; }
        = new List<Citation>();
}