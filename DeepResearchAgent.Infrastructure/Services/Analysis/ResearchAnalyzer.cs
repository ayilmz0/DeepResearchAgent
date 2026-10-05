using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Analysis;

public class ResearchAnalyzer : IResearchAnalyzer
{
    private readonly IAIClient _aiClient;

    public ResearchAnalyzer(IAIClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<IReadOnlyList<Fact>> AnalyzeAsync(
        Source source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Content))
        {
            return [];
        }

        var prompt = $"""
            Analyze the following web source.

            Extract factual claims that are explicitly
            supported by the provided content.

            Rules:
            - Do not invent information.
            - Do not make assumptions.
            - Only extract claims supported by the source.
            - Confidence must be between 0.0 and 1.0.
            - If there are no useful factual claims, return an empty array.

            Source title:
            {source.Title}

            Source URL:
            {source.Url}

            Content:
            {source.Content}
            """;

        var response = await _aiClient.GenerateAsync(
            prompt,
            cancellationToken);

        Console.WriteLine("===== GEMINI RESPONSE =====");
        Console.WriteLine(response);
        Console.WriteLine("===========================");

        var extractedFacts =
            JsonSerializer.Deserialize<List<ExtractedFactDto>>(
                response,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (extractedFacts is null)
        {
            return [];
        }

        return extractedFacts
            .Where(x => !string.IsNullOrWhiteSpace(x.Claim))
            .Select(x => new Fact
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                Claim = x.Claim,
                Value = x.Value,
                Confidence = Math.Clamp(
                    x.Confidence,
                    0.0,
                    1.0)
            })
            .ToList();
    }
}