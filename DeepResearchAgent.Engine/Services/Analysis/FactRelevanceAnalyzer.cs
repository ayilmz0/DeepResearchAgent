
using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Engine.Services.Analysis;

public class FactRelevanceAnalyzer : IFactRelevanceAnalyzer
{
    private readonly IAIClient _aiClient;

    private const int BatchSize = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

            Determine whether the given fact directly contributes
            to answering the research topic.

            Research topic:
            {research.Query}

            Fact:
            {fact.Claim}

            Fact value:
            {fact.Value ?? "N/A"}

            Rules:
            - Judge relevance in the context of the research topic.
            - Similar wording alone does not mean relevance.
            - Generic definitions are normally irrelevant unless useful.
            - Do not invent information.
            - Confidence must be between 0.0 and 1.0.
            - Keep the reason concise.

            Return the structured result.
            """;

        var responseSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                isRelevant = new { type = "BOOLEAN" },
                confidence = new { type = "NUMBER" },
                reason = new { type = "STRING" }
            },
            required = new[]
            {
                "isRelevant",
                "confidence",
                "reason"
            }
        };

        var response = await _aiClient.GenerateAsync(
            prompt,
            responseSchema,
            cancellationToken);

        var result =
            JsonSerializer.Deserialize<FactRelevanceResultDto>(
                response,
                JsonOptions);

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Fact relevance response boş veya geçersiz. Response: {response}");
        }

        result.Confidence = Math.Clamp(result.Confidence, 0.0, 1.0);

        return result;
    }

    public async Task<IReadOnlyList<BatchFactRelevanceResultDto>>
        AnalyzeBatchAsync(
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

        var batches = facts
            .Select((fact, index) => new { fact, index })
            .GroupBy(x => x.index / BatchSize)
            .Select(group => group.Select(x => x.fact).ToList())
            .ToList();

        Console.WriteLine($"Total NVIDIA requests: {batches.Count}");

        var results = new List<BatchFactRelevanceResultDto>();

        for (var batchIndex = 0;
             batchIndex < batches.Count;
             batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = batches[batchIndex];

            Console.WriteLine();
            Console.WriteLine(
                $"Processing batch {batchIndex + 1}/{batches.Count}");
            Console.WriteLine($"Facts in batch: {batch.Count}");

            var prompt = BuildBatchPrompt(research, batch);
            var responseSchema = BuildBatchResponseSchema();

            Console.WriteLine("NVIDIA batch relevance request gönderiliyor...");
            Console.WriteLine($"Prompt length: {prompt.Length}");

            string response;

            try
            {
                response = await _aiClient.GenerateAsync(
                    prompt,
                    responseSchema,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"NVIDIA relevance request başarısız: {ex}");

                // API hatasını "bütün fact'ler ilgisiz" olarak göstermiyoruz.
                throw new InvalidOperationException(
                    $"Fact relevance batch {batchIndex + 1} başarısız oldu.",
                    ex);
            }

            BatchFactRelevanceResponseDto? parsedResponse;

            try
            {
                parsedResponse =
                    JsonSerializer.Deserialize<BatchFactRelevanceResponseDto>(
                        response,
                        JsonOptions);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parse hatası: {ex.Message}");
                Console.WriteLine($"Model response: {response}");

                throw new InvalidOperationException(
                    "NVIDIA batch relevance JSON yanıtı parse edilemedi.",
                    ex);
            }

            if (parsedResponse?.Results is null)
            {
                Console.WriteLine($"Geçersiz model response: {response}");

                throw new InvalidOperationException(
                    "Batch relevance yanıtında 'results' alanı bulunamadı.");
            }

            var resultsByIndex =
                new Dictionary<int, BatchFactRelevanceItemDto>();

            foreach (var item in parsedResponse.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // FactIndex 1 tabanlıdır.
                if (item.FactIndex < 1 || item.FactIndex > batch.Count)
                {
                    throw new InvalidOperationException(
                        $"Model geçersiz FactIndex döndürdü: {item.FactIndex}");
                }

                if (!resultsByIndex.TryAdd(item.FactIndex, item))
                {
                    throw new InvalidOperationException(
                        $"Model aynı FactIndex değerini birden fazla döndürdü: {item.FactIndex}");
                }
            }

            // Her fact için tam olarak bir sonuç olmasını zorunlu tutuyoruz.
            if (resultsByIndex.Count != batch.Count)
            {
                var missingIndexes = Enumerable
                    .Range(1, batch.Count)
                    .Where(index => !resultsByIndex.ContainsKey(index))
                    .ToList();

                throw new InvalidOperationException(
                    "Bazı fact'ler için relevance sonucu dönmedi. " +
                    $"Eksik FactIndex değerleri: {string.Join(", ", missingIndexes)}");
            }

            for (var index = 1; index <= batch.Count; index++)
            {
                var fact = batch[index - 1];
                var item = resultsByIndex[index];

                var result = new BatchFactRelevanceResultDto
                {
                    // Gerçek ID modelden değil, kendi fact listemizden geliyor.
                    FactId = fact.Id,
                    IsRelevant = item.IsRelevant,
                    Confidence = Math.Clamp(item.Confidence, 0.0, 1.0),
                    Reason = item.Reason ?? string.Empty
                };

                results.Add(result);

                Console.WriteLine();
                Console.WriteLine("===== BATCH FACT RELEVANCE RESULT =====");
                Console.WriteLine($"Fact ID: {result.FactId}");
                Console.WriteLine($"Fact index: {index}");
                Console.WriteLine($"Claim: {fact.Claim}");
                Console.WriteLine($"Relevant: {result.IsRelevant}");
                Console.WriteLine($"Confidence: {result.Confidence}");
                Console.WriteLine($"Reason: {result.Reason}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT RELEVANCE ANALYSIS COMPLETED");
        Console.WriteLine("======================================");
        Console.WriteLine($"Total relevance results: {results.Count}");
        Console.WriteLine($"Relevant facts: {results.Count(x => x.IsRelevant)}");
        Console.WriteLine($"Irrelevant facts: {results.Count(x => !x.IsRelevant)}");

        return results;
    }

    private static string BuildBatchPrompt(
        Research research,
        IReadOnlyList<Fact> facts)
    {
        var factBlocks = facts.Select(
            (fact, index) =>
                $"""
                ------------------------------
                FACT INDEX: {index + 1}
                ------------------------------
                Claim: {fact.Claim}
                Value: {fact.Value ?? "N/A"}
                """);

        var factsText = string.Join("\n\n", factBlocks);

        return
            $$"""
            You are a research fact relevance analyzer.

            Determine whether each fact directly contributes
            to answering the research topic.

            Research topic:
            {{research.Query}}

            Rules:
            - Analyze each fact independently.
            - A fact must meaningfully contribute to the research topic.
            - Similar words alone do not establish relevance.
            - A fact from the same broad domain is not automatically relevant.
            - Generic definitions are normally irrelevant unless useful.
            - Do not invent information or rewrite the claims.
            - Confidence must be between 0.0 and 1.0.
            - Keep each reason concise.
            - Return exactly one result for every FACT INDEX.
            - Copy each FACT INDEX exactly.
            - Do not return Fact IDs.
            - Do not omit any fact.

            FACTS:
            {{factsText}}

            Return a JSON object with this structure:
            {
              "results": [
                {
                  "factIndex": 1,
                  "isRelevant": true,
                  "confidence": 0.95,
                  "reason": "Directly contributes to the research topic."
                }
              ]
            }
            """;
    }

    private static object BuildBatchResponseSchema()
    {
        return new
        {
            type = "OBJECT",
            properties = new
            {
                results = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            factIndex = new { type = "INTEGER" },
                            isRelevant = new { type = "BOOLEAN" },
                            confidence = new { type = "NUMBER" },
                            reason = new { type = "STRING" }
                        },
                        required = new[]
                        {
                            "factIndex",
                            "isRelevant",
                            "confidence",
                            "reason"
                        }
                    }
                }
            },
            required = new[] { "results" }
        };
    }

    private sealed class BatchFactRelevanceResponseDto
    {
        public List<BatchFactRelevanceItemDto> Results { get; set; } = [];
    }

    private sealed class BatchFactRelevanceItemDto
    {
        public int FactIndex { get; set; }

        public bool IsRelevant { get; set; }

        public double Confidence { get; set; }

        public string? Reason { get; set; }
    }
}
