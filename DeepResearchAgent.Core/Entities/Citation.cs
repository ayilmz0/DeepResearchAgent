namespace DeepResearchAgent.Core.Entities;

public class Citation
{
    public Guid Id { get; set; }

    public Guid FactId { get; set; }

    public Guid SourceId { get; set; }

    public int? Page { get; set; }

    public string? Quote { get; set; }

    public Fact Fact { get; set; } = null!;

    public Source Source { get; set; } = null!;
}