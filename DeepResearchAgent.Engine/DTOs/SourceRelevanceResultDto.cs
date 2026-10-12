namespace DeepResearchAgent.Engine.DTOs;

public class SourceRelevanceResultDto
{
    public Guid SourceId { get; set; }

    public bool IsRelevant { get; set; }

    // Kaynağın araştırma konusuyla ne kadar alakalı olduğu: 0–1.
    public double RelevanceScore { get; set; }

    // AI değerlendirmesine duyulan güven: 0–1.
    public double Confidence { get; set; }

    public string Reason { get; set; } = string.Empty;
}
