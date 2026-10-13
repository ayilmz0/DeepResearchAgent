
namespace DeepResearchAgent.Engine.DTOs;

public sealed record ResearchChunkSearchResultDto(
    Guid SourceId,
    string SourceTitle,
    string SourceUrl,
    int ChunkIndex,
    string Text,
    double Score);
