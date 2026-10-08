using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Analysis;

public class ResearchAnalyzer : IResearchAnalyzer
{
    private readonly IAIClient _aiClient;
    private readonly IFactRelevanceAnalyzer _factRelevanceAnalyzer;

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
            return [];
        }

        var prompt =
    $"""
    You are a factual information extraction system.

    Your task is to extract concrete factual claims from the source.

    Research topic:
    {research.Query}

    Source title:
    {source.Title}

    Source URL:
    {source.Url}

    Source content:
    {source.Content}

    EXTRACTION RULES:

    - Extract concrete factual claims stated in the source.
    - Extract statistics, measurements, percentages, dates,
      trends, findings, reported effects, relationships and
      important statements.
    - Extract claims that may potentially contribute to the
      research topic.
    - Do not require the fact to directly answer the entire
      research question.
    - Do not perform relevance filtering.
    - Do not decide whether the fact is useful for the final report.
    - That decision will be performed by a separate relevance
      analysis stage.
    - Do not invent information.
    - Do not make assumptions.
    - Every claim must be explicitly supported by the source.
    - Do not generate information that is not present in the source.
    - Avoid duplicate or nearly identical claims.
    - Do not extract navigation menus, advertisements or UI text.
    - Do not extract author names unless they are part of a
      meaningful factual claim.
    - Do not extract generic definitions unless the definition
      itself contains meaningful factual information.
    - Prefer specific and verifiable statements over vague statements.
    - Extract at most 8 facts from this source.
    - Confidence must be between 0.0 and 1.0.
    - If the source contains no concrete factual claims,
      return an empty array.

    IMPORTANT:

    Even if a fact is only potentially related to the research topic,
    extract it if it is a concrete factual claim.

    The next pipeline stage will determine whether the fact is
    actually relevant to the research topic.

    Return ONLY the structured JSON array.
    """;

        var responseSchema =
            new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        claim =
                            new
                            {
                                type = "STRING"
                            },

                        value =
                            new
                            {
                                type = "STRING",
                                nullable = true
                            },

                        confidence =
                            new
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

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("GEMINI FACT EXTRACTION RESPONSE");
        Console.WriteLine("======================================");
        Console.WriteLine(response);
        Console.WriteLine("======================================");

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

        var facts =
            extractedFacts
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Claim))
                .Select(x =>
                    new Fact
                    {
                        Id = Guid.NewGuid(),
                        SourceId = source.Id,
                        Claim = x.Claim.Trim(),
                        Value = x.Value,
                        Confidence =
                            Math.Clamp(
                                x.Confidence,
                                0.0,
                                1.0)
                    })
                .ToList();

        if (facts.Count == 0)
        {
            return [];
        }

        Console.WriteLine();
        Console.WriteLine(
            "======================================");
        Console.WriteLine(
            "FACT RELEVANCE BATCH ANALYSIS");
        Console.WriteLine(
            "======================================");

        Console.WriteLine(
            $"Extracted facts: {facts.Count}");

        IReadOnlyList<BatchFactRelevanceResultDto>
            relevanceResults;

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
            Console.WriteLine();
            Console.WriteLine(
                "Fact relevance batch analysis başarısız.");

            Console.WriteLine(
                $"Hata: {ex.Message}");

            return [];
        }

        var relevanceDictionary =
            relevanceResults
                .GroupBy(x => x.FactId)
                .ToDictionary(
                    x => x.Key,
                    x => x.First());

        var relevantFacts =
            new List<Fact>();

        foreach (var fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!relevanceDictionary.TryGetValue(
                    fact.Id,
                    out var relevance))
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"Fact relevance sonucu bulunamadı: " +
                    $"{fact.Id}");

                continue;
            }

            Console.WriteLine();
            Console.WriteLine(
                "===== FACT RELEVANCE =====");

            Console.WriteLine(
                $"Fact: {fact.Claim}");

            Console.WriteLine(
                $"Relevant: {relevance.IsRelevant}");

            Console.WriteLine(
                $"Confidence: {relevance.Confidence}");

            Console.WriteLine(
                $"Reason: {relevance.Reason}");

            if (relevance.IsRelevant)
            {
                relevantFacts.Add(fact);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Relevant facts from source: " +
            $"{relevantFacts.Count}");

        return relevantFacts;
    }
}