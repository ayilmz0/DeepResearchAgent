using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Verification;

public class FactVerifier : IFactVerifier
{
    private readonly IAIClient _aiClient;

    public FactVerifier(IAIClient aiClient)
    {
        _aiClient = aiClient;
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

        // --------------------------------------------------
        // 1. İlgili evidence'ları bul
        // --------------------------------------------------

        var relatedEvidence = relatedFacts
            .Where(x => x.Id != fact.Id)
            .Where(x => x.SourceId != fact.SourceId)
            .Where(x => AreRelated(fact, x))
            .Take(5)
            .ToList();

        Console.WriteLine(
            $"İlgili evidence sayısı: {relatedEvidence.Count}");

        // --------------------------------------------------
        // 2. Yeterli evidence yoksa Gemini çağırma
        // --------------------------------------------------

        if (relatedEvidence.Count == 0)
        {
            Console.WriteLine(
                "Bu fact için başka kaynaklarda " +
                "ilgili evidence bulunamadı.");

            Console.WriteLine(
                "Gemini verification atlanıyor.");

            Console.WriteLine(
                "===== FACT VERIFIER SKIPPED =====");

            return new FactVerificationResultDto
            {
                Status = FactVerificationStatus.InsufficientEvidence,
                VerificationConfidence = 0,
                SupportingSourceCount = 0,
                ContradictingSourceCount = 0,
                Summary =
                    "Bu fact için başka kaynaklarda " +
                    "yeterli ilgili evidence bulunamadı."
            };
        }

        // --------------------------------------------------
        // 3. Evidence metnini oluştur
        // --------------------------------------------------

        var evidence = relatedEvidence
            .Select(x => $"""
                Source:
                {x.Source.Title}

                URL:
                {x.Source.Url}

                Claim:
                {x.Claim}

                Value:
                {x.Value}
                """);

        var evidenceText = string.Join(
            "\n\n--------------------\n\n",
            evidence);

        Console.WriteLine(
            $"Evidence uzunluğu: {evidenceText.Length}");

        // --------------------------------------------------
        // 4. Verification prompt
        // --------------------------------------------------

        var prompt = $"""
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

        // --------------------------------------------------
        // 5. Gemini response schema
        // --------------------------------------------------

        var responseSchema = new
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

                verificationConfidence = new
                {
                    type = "NUMBER"
                },

                supportingSourceCount = new
                {
                    type = "INTEGER"
                },

                contradictingSourceCount = new
                {
                    type = "INTEGER"
                },

                summary = new
                {
                    type = "STRING"
                }
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

        // --------------------------------------------------
        // 6. Gemini request
        // --------------------------------------------------

        Console.WriteLine(
            "Gemini verification request gönderiliyor...");

        var response =
            await _aiClient.GenerateAsync(
                prompt,
                responseSchema,
                cancellationToken);

        Console.WriteLine(
            "Gemini response alındı.");

        Console.WriteLine(
            $"Response: {response}");

        // --------------------------------------------------
        // 7. Response parse
        // --------------------------------------------------

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

        // --------------------------------------------------
        // 8. Güvenli değer kontrolü
        // --------------------------------------------------

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

        // --------------------------------------------------
        // 9. Status - evidence mantığı kontrolü
        // --------------------------------------------------

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

        // --------------------------------------------------
        // 10. Sonucu logla
        // --------------------------------------------------

        Console.WriteLine(
            $"Verification Status: {result.Status}");

        Console.WriteLine(
            $"Verification Confidence: " +
            $"{result.VerificationConfidence}");

        Console.WriteLine(
            $"Supporting Sources: " +
            $"{result.SupportingSourceCount}");

        Console.WriteLine(
            $"Contradicting Sources: " +
            $"{result.ContradictingSourceCount}");

        Console.WriteLine(
            $"Summary: {result.Summary}");

        Console.WriteLine(
            "===== FACT VERIFIER SUCCESS =====");

        return result;
    }

    // ======================================================
    // RELATED FACT DETECTION
    // ======================================================

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

    // ======================================================
    // IMPORTANT WORD EXTRACTION
    // ======================================================

    private static HashSet<string> GetImportantWords(
        string text)
    {
        var stopWords = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // English
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

            // Turkish
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
            "olan",
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
}