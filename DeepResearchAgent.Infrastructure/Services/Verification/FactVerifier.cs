using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepResearchAgent.Infrastructure.Services.Verification;

public class FactVerifier : IFactVerifier
{
    private readonly IAIClient _aiClient;

    private const int BatchSize = 5;
    private const int MaxEvidencePerFact = 5;

    public FactVerifier(IAIClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<IReadOnlyList<BatchFactVerificationResultDto>> VerifyBatchAsync(
        IReadOnlyList<Fact> facts,
        IReadOnlyList<Fact> allFacts,
        CancellationToken cancellationToken = default)
    {
        if (facts.Count == 0)
        {
            return [];
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT VERIFICATION STARTED");
        Console.WriteLine("======================================");
        Console.WriteLine($"Toplam fact: {facts.Count}");
        Console.WriteLine($"Batch size: {BatchSize}");

        var results = new List<BatchFactVerificationResultDto>();

        var batches =
            facts
                .Select((fact, index) => new { fact, index })
                .GroupBy(x => x.index / BatchSize)
                .Select(x => x.Select(y => y.fact).ToList())
                .ToList();

        Console.WriteLine(
            $"Toplam Gemini batch request sayısı: {batches.Count}");

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine(
                $"Processing batch: {results.Count / BatchSize + 1}");
            Console.WriteLine(
                $"Batch fact sayısı: {batch.Count}");
            Console.WriteLine("--------------------------------------");

            var factsWithEvidence =
                new List<FactEvidence>();

            foreach (var fact in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relatedEvidence =
                    allFacts
                        .Where(x => x.Id != fact.Id)
                        .Where(x => x.SourceId != fact.SourceId)
                        .Where(x => AreRelated(fact, x))
                        .Take(MaxEvidencePerFact)
                        .ToList();

                Console.WriteLine();
                Console.WriteLine($"Fact ID: {fact.Id}");
                Console.WriteLine($"Claim: {fact.Claim}");
                Console.WriteLine(
                    $"Related evidence: {relatedEvidence.Count}");

                if (relatedEvidence.Count == 0)
                {
                    Console.WriteLine(
                        "Evidence bulunamadı. Gemini çağrısı yapılmayacak.");

                    results.Add(
                        CreateInsufficientEvidenceResult(
                            fact,
                            "Bu fact için başka kaynaklarda yeterli ilgili evidence bulunamadı."));

                    continue;
                }

                factsWithEvidence.Add(
                    new FactEvidence
                    {
                        Fact = fact,
                        Evidence = relatedEvidence
                    });
            }

            if (factsWithEvidence.Count == 0)
            {
                Console.WriteLine(
                    "Bu batch içinde Gemini verification gerektiren fact yok.");

                continue;
            }

            var prompt =
                BuildBatchPrompt(factsWithEvidence);

            var responseSchema =
                BuildBatchResponseSchema();

            Console.WriteLine();
            Console.WriteLine(
                $"Gemini batch verification request gönderiliyor.");
            Console.WriteLine(
                $"Verification yapılacak fact sayısı: " +
                $"{factsWithEvidence.Count}");
            Console.WriteLine(
                $"Prompt uzunluğu: {prompt.Length}");

            string response;

            try
            {
                response =
                    await _aiClient.GenerateAsync(
                        prompt,
                        responseSchema,
                        cancellationToken);

                Console.WriteLine();
                Console.WriteLine("======================================");
                Console.WriteLine("GEMINI BATCH VERIFICATION RESPONSE");
                Console.WriteLine("======================================");
                Console.WriteLine(response);
                Console.WriteLine("======================================");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Batch Gemini verification başarısız.");
                Console.WriteLine(
                    $"Hata: {ex.Message}");

                foreach (var factEvidence in factsWithEvidence)
                {
                    results.Add(
                        CreateInsufficientEvidenceResult(
                            factEvidence.Fact,
                            "Batch fact verification sırasında Gemini çağrısı başarısız oldu."));
                }

                continue;
            }

            Console.WriteLine(
                "Gemini batch verification response alındı.");

            Console.WriteLine(
                $"Response length: {response.Length}");

            List<BatchFactVerificationResultDto>? batchResults;

            try
            {
                batchResults =
                JsonSerializer.Deserialize<
                List<BatchFactVerificationResultDto>>(
                    response,
                    new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters =
                {
                    new JsonStringEnumConverter()
                }
                });
            }
            catch (JsonException ex)
            {
                Console.WriteLine();
                Console.WriteLine("======================================");
                Console.WriteLine("VERIFICATION JSON PARSE ERROR");
                Console.WriteLine("======================================");

                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Path: {ex.Path}");
                Console.WriteLine($"LineNumber: {ex.LineNumber}");
                Console.WriteLine($"BytePositionInLine: {ex.BytePositionInLine}");

                Console.WriteLine();
                Console.WriteLine("RAW RESPONSE:");
                Console.WriteLine(response);

                Console.WriteLine("======================================");

                batchResults = null;
            }

            if (batchResults is null)
            {
                foreach (var factEvidence in factsWithEvidence)
                {
                    results.Add(
                        CreateInsufficientEvidenceResult(
                            factEvidence.Fact,
                            "Batch verification sonucu parse edilemedi."));
                }

                continue;
            }

            var validFactIds =
                factsWithEvidence
                    .Select(x => x.Fact.Id)
                    .ToHashSet();

            foreach (var verification in batchResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!validFactIds.Contains(verification.FactId))
                {
                    Console.WriteLine(
                        $"Bilinmeyen FactId döndü: " +
                        $"{verification.FactId}");

                    continue;
                }

                var factEvidence =
                    factsWithEvidence.FirstOrDefault(
                        x => x.Fact.Id == verification.FactId);

                if (factEvidence is null)
                {
                    continue;
                }

                verification.VerificationConfidence =
                    Math.Clamp(
                        verification.VerificationConfidence,
                        0.0,
                        1.0);

                verification.SupportingSourceCount =
                    Math.Clamp(
                        verification.SupportingSourceCount,
                        0,
                        factEvidence.Evidence.Count);

                verification.ContradictingSourceCount =
                    Math.Clamp(
                        verification.ContradictingSourceCount,
                        0,
                        factEvidence.Evidence.Count);

                if (verification.SupportingSourceCount == 0 &&
                    verification.ContradictingSourceCount == 0)
                {
                    verification.Status =
                        FactVerificationStatus.InsufficientEvidence;

                    verification.VerificationConfidence =
                        Math.Min(
                            verification.VerificationConfidence,
                            0.30);
                }

                results.Add(verification);

                Console.WriteLine();
                Console.WriteLine("--------------------------------------");
                Console.WriteLine("BATCH VERIFICATION RESULT");
                Console.WriteLine(
                    $"Fact ID: {verification.FactId}");
                Console.WriteLine(
                    $"Status: {verification.Status}");
                Console.WriteLine(
                    $"Confidence: {verification.VerificationConfidence}");
                Console.WriteLine(
                    $"Supporting Sources: " +
                    $"{verification.SupportingSourceCount}");
                Console.WriteLine(
                    $"Contradicting Sources: " +
                    $"{verification.ContradictingSourceCount}");
                Console.WriteLine(
                    $"Summary: {verification.Summary}");
                Console.WriteLine("--------------------------------------");
            }

            var returnedFactIds =
                batchResults
                    .Select(x => x.FactId)
                    .ToHashSet();

            foreach (var factEvidence in factsWithEvidence)
            {
                if (returnedFactIds.Contains(factEvidence.Fact.Id))
                {
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"Fact için verification sonucu dönmedi: " +
                    $"{factEvidence.Fact.Id}");

                results.Add(
                    CreateInsufficientEvidenceResult(
                        factEvidence.Fact,
                        "Gemini batch response içinde bu fact için verification sonucu bulunamadı."));
            }
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT VERIFICATION COMPLETED");
        Console.WriteLine("======================================");
        Console.WriteLine(
            $"Toplam verification sonucu: {results.Count}");

        return results;
    }

    public async Task<FactVerificationResultDto> VerifyAsync(
        Fact fact,
        IReadOnlyList<Fact> relatedFacts,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine("===== FACT VERIFIER START =====");
        Console.WriteLine($"Fact ID: {fact.Id}");
        Console.WriteLine($"Claim: {fact.Claim}");

        var relatedEvidence =
            relatedFacts
                .Where(x => x.Id != fact.Id)
                .Where(x => x.SourceId != fact.SourceId)
                .Where(x => AreRelated(fact, x))
                .Take(MaxEvidencePerFact)
                .ToList();

        Console.WriteLine(
            $"İlgili evidence sayısı: {relatedEvidence.Count}");

        if (relatedEvidence.Count == 0)
        {
            Console.WriteLine(
                "Bu fact için başka kaynaklarda ilgili evidence bulunamadı.");

            Console.WriteLine(
                "Gemini verification atlanıyor.");

            Console.WriteLine(
                "===== FACT VERIFIER SKIPPED =====");

            return new FactVerificationResultDto
            {
                Status =
                    FactVerificationStatus.InsufficientEvidence,

                VerificationConfidence = 0,

                SupportingSourceCount = 0,

                ContradictingSourceCount = 0,

                Summary =
                    "Bu fact için başka kaynaklarda " +
                    "yeterli ilgili evidence bulunamadı."
            };
        }

        var evidence =
            relatedEvidence.Select(
                x =>
                    $"""
                    Source:
                    {x.Source.Title}

                    URL:
                    {x.Source.Url}

                    Claim:
                    {x.Claim}

                    Value:
                    {x.Value}
                    """);

        var evidenceText =
            string.Join(
                "\n\n--------------------\n\n",
                evidence);

        var prompt =
            $"""
            You are a strict fact verification system.

            Your task is to verify the claim below using
            ONLY evidence extracted from OTHER sources.

            You must determine whether the evidence:

            1. Supports the claim
            2. Partially supports the claim
            3. Contradicts the claim
            4. Is insufficient to verify the claim

            IMPORTANT RULES:

            - Do not assume the claim is true.
            - Do not invent evidence.
            - Use ONLY the provided evidence.
            - Do not use your general knowledge.
            - Do not use information outside the provided evidence.
            - Evidence must directly support or contradict the claim.
            - Semantic similarity alone is NOT sufficient.
            - A source being about the same industry is NOT sufficient.
            - A source containing similar keywords is NOT sufficient.
            - If evidence is related to the topic but does not actually
              support the specific claim, classify it as
              InsufficientEvidence.
            - If only part of the claim is supported, classify it as
              PartiallySupported.
            - If the evidence directly contradicts the claim,
              classify it as Contradicted.
            - If the evidence directly supports the claim,
              classify it as Supported.
            - If evidence is insufficient, verification confidence
              must be low.
            - Verification confidence must be between 0.0 and 1.0.
            - SupportingSourceCount must count only sources that
              directly support the claim.
            - ContradictingSourceCount must count only sources that
              directly contradict the claim.
            - Do not count irrelevant sources.
            - The same source must not be counted more than once.

            STATUS DEFINITIONS:

            Supported:
            The provided evidence directly supports the main claim.

            PartiallySupported:
            The evidence supports only part of the claim,
            but does not establish the entire claim.

            Contradicted:
            The provided evidence directly conflicts with
            the main claim.

            InsufficientEvidence:
            The evidence is irrelevant, too weak, indirect,
            incomplete, or does not establish whether the claim
            is true or false.

            Claim to verify:

            {fact.Claim}

            Value:

            {fact.Value}

            Evidence from other sources:

            {evidenceText}

            Return the verification result.
            """;

        var responseSchema =
            new
            {
                type = "OBJECT",
                properties = new
                {
                    status = new
                    {
                        type = "STRING",
                        @enum = new[]
                        {
                            "Supported",
                            "PartiallySupported",
                            "Contradicted",
                            "InsufficientEvidence"
                        }
                    },
                    verificationConfidence =
                        new { type = "NUMBER" },
                    supportingSourceCount =
                        new { type = "INTEGER" },
                    contradictingSourceCount =
                        new { type = "INTEGER" },
                    summary =
                        new { type = "STRING" }
                },
                required = new[]
                {
                    "status",
                    "verificationConfidence",
                    "supportingSourceCount",
                    "contradictingSourceCount",
                    "summary"
                }
            };

        Console.WriteLine(
            "Gemini verification request gönderiliyor...");

        var response =
            await _aiClient.GenerateAsync(
                prompt,
                responseSchema,
                cancellationToken);

        Console.WriteLine(
            "Gemini response alındı.");

        var result =
            JsonSerializer.Deserialize<FactVerificationResultDto>(
                response,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (result is null)
        {
            throw new InvalidOperationException(
                "Gemini verification sonucu parse edilemedi.");
        }

        result.VerificationConfidence =
            Math.Clamp(
                result.VerificationConfidence,
                0.0,
                1.0);

        result.SupportingSourceCount =
            Math.Clamp(
                result.SupportingSourceCount,
                0,
                relatedEvidence.Count);

        result.ContradictingSourceCount =
            Math.Clamp(
                result.ContradictingSourceCount,
                0,
                relatedEvidence.Count);

        if (result.SupportingSourceCount == 0 &&
            result.ContradictingSourceCount == 0)
        {
            result.Status =
                FactVerificationStatus.InsufficientEvidence;

            result.VerificationConfidence =
                Math.Min(
                    result.VerificationConfidence,
                    0.30);
        }

        return result;
    }

    private static string BuildBatchPrompt(
        IReadOnlyList<FactEvidence> factsWithEvidence)
    {
        var factBlocks =
            factsWithEvidence.Select(
                (item, index) =>
                {
                    var evidenceText =
                        string.Join(
                            "\n\n--------------------\n\n",
                            item.Evidence.Select(
                                evidence =>
                                    $"""
                                    Source:
                                    {evidence.Source.Title}

                                    URL:
                                    {evidence.Source.Url}

                                    Claim:
                                    {evidence.Claim}

                                    Value:
                                    {evidence.Value}
                                    """));

                    return
                        $"""
                        ==============================
                        FACT {index + 1}
                        ==============================

                        Fact ID:
                        {item.Fact.Id}

                        Claim:
                        {item.Fact.Claim}

                        Value:
                        {item.Fact.Value}

                        Evidence from other sources:

                        {evidenceText}
                        """;
                });

        var factsText =
            string.Join(
                "\n\n",
                factBlocks);

        return
            $"""
            You are a strict batch fact verification system.

            You will receive multiple factual claims.

            Verify EACH fact independently using ONLY the evidence
            provided for that fact.

            IMPORTANT RULES:

            - Do not use your general knowledge.
            - Do not invent evidence.
            - Do not assume a claim is true.
            - Use ONLY the evidence provided for the corresponding fact.
            - Do not use evidence belonging to another fact.
            - Evidence must directly support or contradict the claim.
            - Semantic similarity alone is NOT sufficient.
            - Similar keywords alone are NOT sufficient.
            - A source being about the same industry is NOT sufficient.
            - If evidence is related to the topic but does not directly
              establish the claim, use InsufficientEvidence.
            - If only part of the claim is supported, use
              PartiallySupported.
            - If evidence directly supports the claim, use Supported.
            - If evidence directly contradicts the claim, use Contradicted.
            - If evidence is insufficient, confidence must be low.
            - VerificationConfidence must be between 0.0 and 1.0.
            - SupportingSourceCount must count ONLY sources that directly
              support the specific claim.
            - ContradictingSourceCount must count ONLY sources that
              directly contradict the specific claim.
            - Never count the same source more than once.
            - Return exactly one result for every Fact ID provided.
            - Never invent a Fact ID.
            - FactId must be copied exactly from the input.

            STATUS DEFINITIONS:

            Supported:
            The provided evidence directly supports the main claim.

            PartiallySupported:
            The evidence supports only part of the claim,
            but does not establish the entire claim.

            Contradicted:
            The provided evidence directly conflicts with
            the main claim.

            InsufficientEvidence:
            The evidence is irrelevant, too weak, indirect,
            incomplete, or does not establish whether the claim
            is true or false.

            FACTS TO VERIFY:

            {factsText}

            Return ONLY a JSON array containing the verification
            result for every provided Fact ID.
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

                    status =
                        new
                        {
                            type = "STRING",
                            @enum = new[]
                            {
                                "Supported",
                                "PartiallySupported",
                                "Contradicted",
                                "InsufficientEvidence"
                            }
                        },

                    verificationConfidence =
                        new
                        {
                            type = "NUMBER"
                        },

                    supportingSourceCount =
                        new
                        {
                            type = "INTEGER"
                        },

                    contradictingSourceCount =
                        new
                        {
                            type = "INTEGER"
                        },

                    summary =
                        new
                        {
                            type = "STRING"
                        }
                },
                required = new[]
                {
                    "factId",
                    "status",
                    "verificationConfidence",
                    "supportingSourceCount",
                    "contradictingSourceCount",
                    "summary"
                }
            }
        };
    }

    private static BatchFactVerificationResultDto
        CreateInsufficientEvidenceResult(
            Fact fact,
            string summary)
    {
        return new BatchFactVerificationResultDto
        {
            FactId = fact.Id,

            Status =
                FactVerificationStatus.InsufficientEvidence,

            VerificationConfidence = 0,

            SupportingSourceCount = 0,

            ContradictingSourceCount = 0,

            Summary = summary
        };
    }

    private static bool AreRelated(
        Fact first,
        Fact second)
    {
        var firstWords =
            GetImportantWords(first.Claim);

        var secondWords =
            GetImportantWords(second.Claim);

        if (firstWords.Count == 0 ||
            secondWords.Count == 0)
        {
            return false;
        }

        var commonWords =
            firstWords
                .Intersect(secondWords)
                .Count();

        return commonWords >= 2;
    }

    private static HashSet<string> GetImportantWords(
        string text)
    {
        var stopWords =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "the",
                "and",
                "that",
                "this",
                "with",
                "from",
                "for",
                "are",
                "was",
                "were",
                "have",
                "has",
                "been",
                "being",
                "into",
                "about",
                "than",
                "then",
                "their",
                "there",
                "which",
                "while",
                "where",
                "when",
                "what",
                "also",

                "bir",
                "ve",
                "ile",
                "için",
                "olan",
                "olarak",
                "bu",
                "şu",
                "da",
                "de",
                "çok",
                "daha",
                "gibi",
                "ise",
                "olanın",
                "tarafından",
                "üzerinde",
                "arasında",
                "sonra",
                "önce",
                "kadar"
            };

        return text
            .ToLowerInvariant()
            .Split(
                [
                    ' ',
                    ',',
                    '.',
                    ';',
                    ':',
                    '!',
                    '?',
                    '(',
                    ')',
                    '"',
                    '\'',
                    '-',
                    '/',
                    '\\',
                    '\n',
                    '\r',
                    '\t'
                ],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 4)
            .Where(x => !stopWords.Contains(x))
            .ToHashSet();
    }

    private sealed class FactEvidence
    {
        public Fact Fact { get; set; } = null!;

        public IReadOnlyList<Fact> Evidence { get; set; } = [];
    }
}