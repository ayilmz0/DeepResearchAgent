using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using System.Text.Json;

namespace DeepResearchAgent.Infrastructure.Services.Analysis;

public class ResearchAnalyzer : IResearchAnalyzer
{
    private readonly IAIClient _aiClient;
    private readonly IFactRelevanceAnalyzer _factRelevanceAnalyzer;

    public ResearchAnalyzer(IAIClient aiClient, IFactRelevanceAnalyzer factRelevanceAnalyzer)
    {
        _aiClient = aiClient;
        _factRelevanceAnalyzer = factRelevanceAnalyzer;
    }

    public async Task<IReadOnlyList<Fact>> AnalyzeAsync(
        Research research,
        Source source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Content))
        {
            return [];
        }

        var prompt = $"""
    You are a factual information extraction system.

    Your task is to extract only factual claims
    that are directly relevant to the research topic.

    Research topic:
    {research.Query}

    Source title:
    {source.Title}

    Source URL:
    {source.Url}

    Source content:
    {source.Content}

    Rules:

    - Extract only facts directly relevant to the research topic.
    - Ignore unrelated information from the source.
    - Do not extract author names unless the author is relevant
      to the research topic.
    - Do not extract publication dates unless they are relevant
      to the research topic.
    - Do not extract generic definitions unless they directly
      contribute to answering the research topic.
    - Do not invent information.
    - Do not make assumptions.
    - Every claim must be explicitly supported by the source.
    - Prefer important facts, statistics, findings and concrete
      statements.
    - Avoid duplicate or nearly identical claims.
    - Extract at most 8 facts from this source.
    - Confidence must be between 0.0 and 1.0.
    - If the source contains no relevant facts, return an empty array.
    """;

        var responseSchema = new
        {
            type = "ARRAY",

            items = new
            {
                type = "OBJECT",

                properties = new
                {
                    claim = new
                    {
                        type = "STRING"
                    },

                    value = new
                    {
                        type = "STRING",
                        nullable = true
                    },

                    confidence = new
                    {
                        type = "NUMBER"
                    }
                },

                required = new[]
                {
                    "claim",
                    "value",
                    "confidence"
                }
            }
        };

        var response =
            await _aiClient.GenerateAsync(
                prompt,
                responseSchema,
                cancellationToken);

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

        var facts = extractedFacts
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

        var relevantFacts = new List<Fact>();

        foreach (var fact in facts)
        {
            var relevance =
                await _factRelevanceAnalyzer.AnalyzeAsync(
                    research,
                    fact,
                    cancellationToken);

            Console.WriteLine();
            Console.WriteLine("===== FACT RELEVANCE =====");
            Console.WriteLine($"Fact: {fact.Claim}");
            Console.WriteLine($"Relevant: {relevance.IsRelevant}");
            Console.WriteLine($"Confidence: {relevance.Confidence}");
            Console.WriteLine($"Reason: {relevance.Reason}");

            if (relevance.IsRelevant)
            {
                relevantFacts.Add(fact);
            }
        }

        return relevantFacts;
    }
}