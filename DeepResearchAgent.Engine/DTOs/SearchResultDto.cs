namespace DeepResearchAgent.Engine.DTOs;

public class SearchResultDto
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Snippet { get; set; }
}