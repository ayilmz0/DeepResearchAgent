
using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Analysis;

public class ResearchAnalyzer : IResearchAnalyzer
{
    private readonly IAIClient _aiClient;
    private readonly IFactRelevanceAnalyzer _factRelevanceAnalyzer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ResearchAnalyzer(
        IAIClient aiClient,
        IFactRelevanceAnalyzer factRelevanceAnalyzer)
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
            Console.WriteLine(
                $"ANALYZER SKIPPED: İçerik boş. SourceId={source.Id}");

            return [];
        }


        var prompt =
            $$"""
    You are a factual information extraction system.

    Research topic:
    {{research.Query}}

    Source title:
    {{source.Title}}

    Source URL:
    {{source.Url}}

    Source content:
    {{source.Content}}

    TASK:
    Extract concrete factual claims explicitly supported by the source
    that may contribute to the research topic.

    RULES:
    - Extract concrete factual claims, statistics, measurements,
      percentages, dates, trends, findings and reported effects.
    - Do not require a claim to answer the entire research question.
    - Do not perform relevance filtering.
    - Do not invent facts or make unsupported assumptions.
    - Do not extract navigation menus, advertisements or UI text.
    - Avoid duplicate or nearly identical claims.
    - Prefer specific and verifiable statements.
    - Extract at most 8 facts from this source.
    - Confidence must be between 0.0 and 1.0.
    - Value may be null if there is no separate value.
    - If no factual claims exist, return an empty facts array.

    OUTPUT REQUIREMENTS:
    Return only valid JSON.
    The root must be an object with a "facts" array.
    Do not return a root-level JSON array.
    Do not include Markdown fences or explanatory text.

    Expected structure:
    {
      "facts": [
        {
          "claim": "A factual claim supported by the source",
          "value": null,
          "confidence": 0.9
        }
      ]
    }
    """;


        var responseSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                facts = new
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
                }
            },
            required = new[]
            {
                "facts"
            }
        };

        var response = await _aiClient.GenerateAsync(
            prompt,
            responseSchema,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("NVIDIA FACT EXTRACTION RESPONSE");
        Console.WriteLine("======================================");
        Console.WriteLine(response);
        Console.WriteLine("======================================");

        ExtractedFactsResponseDto? extractionResult;

        try
        {
            extractionResult =
                JsonSerializer.Deserialize<ExtractedFactsResponseDto>(
                    response,
                    JsonOptions);
        }
        catch (JsonException ex)
        {
            Console.WriteLine(
                $"Fact extraction JSON parse hatası. SourceId={source.Id}");
            Console.WriteLine(ex);

            throw new InvalidOperationException(
                $"Fact extraction yanıtı geçerli JSON formatında değil. " +
                $"SourceId={source.Id}",
                ex);
        }

        if (extractionResult?.Facts is null)
        {
            Console.WriteLine(
                $"Fact extraction sonucunda facts dizisi yok. " +
                $"SourceId={source.Id}");

            return [];
        }

        var facts = extractionResult.Facts
            .Where(x => !string.IsNullOrWhiteSpace(x.Claim))
            .Select(x => new Fact
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                Source = source,
                Claim = x.Claim.Trim(),
                Value = x.Value,
                Confidence = Math.Clamp(x.Confidence, 0.0, 1.0)
            })
            .ToList();

        Console.WriteLine(
            $"Extracted facts before relevance filtering: {facts.Count}");

        if (facts.Count == 0)
        {
            Console.WriteLine(
                $"Bu kaynakta kullanılabilir fact bulunamadı. " +
                $"SourceId={source.Id}");

            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<BatchFactRelevanceResultDto> relevanceResults;

        try
        {
            relevanceResults =
                await _factRelevanceAnalyzer.AnalyzeBatchAsync(
                    research,
                    facts,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Fact relevance analizi başarısız. SourceId={source.Id}");
            Console.WriteLine(ex);

            throw new InvalidOperationException(
                $"Fact relevance analizi başarısız. SourceId={source.Id}",
                ex);
        }

        var relevanceDictionary = relevanceResults
            .GroupBy(x => x.FactId)
            .ToDictionary(
                group => group.Key,
                group => group.First());

        var relevantFacts = new List<Fact>();

        foreach (var fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!relevanceDictionary.TryGetValue(
                    fact.Id,
                    out var relevance))
            {
                Console.WriteLine(
                    $"FACT RELEVANCE RESULT MISSING | " +
                    $"FactId={fact.Id} | Claim={fact.Claim}");

                continue;
            }

            Console.WriteLine();
            Console.WriteLine("===== FACT RELEVANCE =====");
            Console.WriteLine($"FactId: {fact.Id}");
            Console.WriteLine($"Claim: {fact.Claim}");
            Console.WriteLine($"Relevant: {relevance.IsRelevant}");
            Console.WriteLine($"Confidence: {relevance.Confidence}");
            Console.WriteLine($"Reason: {relevance.Reason}");

            if (relevance.IsRelevant)
            {
                relevantFacts.Add(fact);
            }
        }

        Console.WriteLine(
            $"Fact relevance sonuç sayısı: {relevanceResults.Count}");

        Console.WriteLine(
            $"Relevant facts from source: {relevantFacts.Count}");

        return relevantFacts;
    }

    private sealed class ExtractedFactsResponseDto
    {
        public List<ExtractedFactDto> Facts { get; set; } = [];
    }
}
