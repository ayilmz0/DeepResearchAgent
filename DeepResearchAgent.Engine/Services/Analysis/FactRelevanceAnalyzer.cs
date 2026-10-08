using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Engine.Services.Analysis;

public class FactRelevanceAnalyzer : IFactRelevanceAnalyzer
{
    private readonly IAIClient _aiClient;

    private const int BatchSize = 10;

    public FactRelevanceAnalyzer(IAIClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<FactRelevanceResultDto> AnalyzeAsync(
        Research research,
        Fact fact,
        CancellationToken cancellationToken = default)
    {
        var prompt =
            $"""
            You are a research fact relevance analyzer.

            Your task is to determine whether the given fact
            is directly relevant to the research topic.

            Research topic:
            {research.Query}

            Fact:
            {fact.Claim}

            Fact value:
            {fact.Value}

            IMPORTANT RULES:

            - Determine relevance based on the research topic.
            - The fact must contribute meaningfully to answering
              the research question.
            - A fact is NOT relevant merely because it contains
              similar words.
            - A fact is NOT relevant merely because it belongs
              to the same broad domain.
            - Generic definitions should normally be considered
              irrelevant unless they directly contribute to the
              research topic.
            - Facts about unrelated industries, companies,
              technologies or subjects must be marked irrelevant.
            - Do not invent information.
            - Confidence must be between 0.0 and 1.0.
            - Keep the reason concise.

            Return only the structured result.
            """;

        var responseSchema =
            new
            {
                type = "OBJECT",
                properties = new
                {
                    isRelevant =
                        new
                        {
                            type = "BOOLEAN"
                        },

                    confidence =
                        new
                        {
                            type = "NUMBER"
                        },

                    reason =
                        new
                        {
                            type = "STRING"
                        }
                },
                required = new[]
                {
                    "isRelevant",
                    "confidence",
                    "reason"
                }
            };

        var response =
            await _aiClient.GenerateAsync(
                prompt,
                responseSchema,
                cancellationToken);

        var result =
            JsonSerializer.Deserialize<FactRelevanceResultDto>(
                response,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (result is null)
        {
            throw new InvalidOperationException(
                "Fact relevance sonucu parse edilemedi.");
        }

        result.Confidence =
            Math.Clamp(
                result.Confidence,
                0.0,
                1.0);

        return result;
    }

    public async Task<IReadOnlyList<BatchFactRelevanceResultDto>> AnalyzeBatchAsync(
        Research research,
        IReadOnlyList<Fact> facts,
        CancellationToken cancellationToken = default)
    {
        if (facts.Count == 0)
        {
            return [];
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT RELEVANCE ANALYSIS STARTED");
        Console.WriteLine("======================================");
        Console.WriteLine($"Total facts: {facts.Count}");
        Console.WriteLine($"Batch size: {BatchSize}");

        var results =
            new List<BatchFactRelevanceResultDto>();

        var batches =
            facts
                .Select((fact, index) => new
                {
                    fact,
                    index
                })
                .GroupBy(x => x.index / BatchSize)
                .Select(x =>
                    x.Select(y => y.fact).ToList())
                .ToList();

        Console.WriteLine(
            $"Total Gemini requests: {batches.Count}");

        for (var batchIndex = 0;
             batchIndex < batches.Count;
             batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch =
                batches[batchIndex];

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine(
                $"Processing relevance batch " +
                $"{batchIndex + 1}/{batches.Count}");
            Console.WriteLine(
                $"Facts in batch: {batch.Count}");
            Console.WriteLine("--------------------------------------");

            var prompt =
                BuildBatchPrompt(
                    research,
                    batch);

            var responseSchema =
                BuildBatchResponseSchema();

            string response;

            try
            {
                Console.WriteLine(
                    "Gemini batch relevance request gönderiliyor...");

                Console.WriteLine(
                    $"Prompt length: {prompt.Length}");

                response =
                    await _aiClient.GenerateAsync(
                        prompt,
                        responseSchema,
                        cancellationToken);

                Console.WriteLine(
                    "Gemini batch relevance response alındı.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Batch relevance Gemini request başarısız.");
                Console.WriteLine(
                    $"Hata: {ex.Message}");

                foreach (var fact in batch)
                {
                    results.Add(
                        new BatchFactRelevanceResultDto
                        {
                            FactId = fact.Id,
                            IsRelevant = false,
                            Confidence = 0,
                            Reason =
                                "Fact relevance analysis sırasında " +
                                "Gemini çağrısı başarısız oldu."
                        });
                }

                continue;
            }

            List<BatchFactRelevanceResultDto>? batchResults;

            try
            {
                batchResults =
                    JsonSerializer.Deserialize<
                        List<BatchFactRelevanceResultDto>>(
                        response,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
            }
            catch (JsonException ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Batch relevance response parse edilemedi.");
                Console.WriteLine(
                    $"Hata: {ex.Message}");

                batchResults = null;
            }

            if (batchResults is null)
            {
                foreach (var fact in batch)
                {
                    results.Add(
                        new BatchFactRelevanceResultDto
                        {
                            FactId = fact.Id,
                            IsRelevant = false,
                            Confidence = 0,
                            Reason =
                                "Gemini batch relevance sonucu " +
                                "parse edilemedi."
                        });
                }

                continue;
            }

            var validFactIds =
                batch
                    .Select(x => x.Id)
                    .ToHashSet();

            foreach (var result in batchResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!validFactIds.Contains(result.FactId))
                {
                    Console.WriteLine(
                        $"Unknown FactId returned: " +
                        $"{result.FactId}");

                    continue;
                }

                result.Confidence =
                    Math.Clamp(
                        result.Confidence,
                        0.0,
                        1.0);

                results.Add(result);

                Console.WriteLine();
                Console.WriteLine(
                    "===== BATCH FACT RELEVANCE RESULT =====");

                Console.WriteLine(
                    $"Fact ID: {result.FactId}");

                Console.WriteLine(
                    $"Relevant: {result.IsRelevant}");

                Console.WriteLine(
                    $"Confidence: {result.Confidence}");

                Console.WriteLine(
                    $"Reason: {result.Reason}");
            }

            var returnedFactIds =
                batchResults
                    .Select(x => x.FactId)
                    .ToHashSet();

            foreach (var fact in batch)
            {
                if (returnedFactIds.Contains(fact.Id))
                {
                    continue;
                }

                Console.WriteLine(
                    $"Fact için relevance sonucu dönmedi: " +
                    $"{fact.Id}");

                results.Add(
                    new BatchFactRelevanceResultDto
                    {
                        FactId = fact.Id,
                        IsRelevant = false,
                        Confidence = 0,
                        Reason =
                            "Gemini batch response içinde " +
                            "bu fact için relevance sonucu bulunamadı."
                    });
            }
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT RELEVANCE ANALYSIS COMPLETED");
        Console.WriteLine("======================================");
        Console.WriteLine(
            $"Total relevance results: {results.Count}");

        Console.WriteLine(
            $"Relevant facts: " +
            $"{results.Count(x => x.IsRelevant)}");

        Console.WriteLine(
            $"Irrelevant facts: " +
            $"{results.Count(x => !x.IsRelevant)}");

        return results;
    }

    private static string BuildBatchPrompt(
        Research research,
        IReadOnlyList<Fact> facts)
    {
        var factBlocks =
            facts.Select(
                (fact, index) =>
                    $"""
                    ==============================
                    FACT {index + 1}
                    ==============================

                    Fact ID:
                    {fact.Id}

                    Claim:
                    {fact.Claim}

                    Value:
                    {fact.Value ?? "N/A"}
                    """);

        var factsText =
            string.Join(
                "\n\n",
                factBlocks);

        return
            $"""
            You are a research fact relevance analyzer.

            You will receive multiple facts extracted from sources.

            Your task is to independently determine whether EACH fact
            is directly relevant to the research topic.

            Research topic:

            {research.Query}

            IMPORTANT RULES:

            - Analyze every fact independently.
            - A fact must contribute meaningfully to answering
              the research topic.
            - A fact is NOT relevant merely because it contains
              similar words.
            - A fact is NOT relevant merely because it belongs
              to the same broad domain.
            - Generic definitions should normally be considered
              irrelevant unless they directly contribute to the
              research topic.
            - Facts about unrelated industries, companies,
              technologies or subjects must be marked irrelevant.
            - Do not invent information.
            - Do not change or rewrite the fact.
            - Confidence must be between 0.0 and 1.0.
            - Keep the reason concise.
            - Return exactly one result for every Fact ID.
            - Copy FactId exactly.
            - Do not invent Fact IDs.

            FACTS:

            {factsText}

            Return ONLY a JSON array containing one relevance
            result for every Fact ID.
            """;
    }

    private static object BuildBatchResponseSchema()
    {
        return new
        {
            type = "ARRAY",
            items = new
            {
                type = "OBJECT",
                properties = new
                {
                    factId =
                        new
                        {
                            type = "STRING"
                        },

                    isRelevant =
                        new
                        {
                            type = "BOOLEAN"
                        },

                    confidence =
                        new
                        {
                            type = "NUMBER"
                        },

                    reason =
                        new
                        {
                            type = "STRING"
                        }
                },
                required = new[]
                {
                    "factId",
                    "isRelevant",
                    "confidence",
                    "reason"
                }
            }
        };
    }
}