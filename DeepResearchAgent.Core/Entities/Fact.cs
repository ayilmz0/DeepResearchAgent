namespace DeepResearchAgent.Core.Entities;

public class Fact
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public string Claim { get; set; } = string.Empty;

    public string? Value { get; set; }

    public double Confidence { get; set; }

    public Source Source { get; set; } = null!;

    public ICollection<Citation> Citations { get; set; } = new List<Citation>();
}