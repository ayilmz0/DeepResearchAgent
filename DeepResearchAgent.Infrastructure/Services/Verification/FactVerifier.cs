using System.Text.Json;
using DeepResearchAgent.Core.Entities;
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

        var evidence = relatedFacts
            .Where(x => x.Id != fact.Id)
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

        var prompt = $"""
            You are a fact verification system.

            Verify the following claim using evidence
            extracted from other sources.

            IMPORTANT RULES:

            - Do not assume the claim is true.
            - Do not invent evidence.
            - Identify supporting evidence.
            - Identify contradicting evidence.
            - If there is insufficient evidence,
              lower the verification confidence.
            - Verification confidence must be between
              0.0 and 1.0.

            Claim to verify:

            {fact.Claim}

            Value:

            {fact.Value}

            Evidence from other sources:

            {evidenceText}

            Return the verification result.
            """;

        var responseSchema = new
        {
            type = "OBJECT",

            properties = new
            {
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

        Console.WriteLine(
            $"Response: {response}");

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
            Math.Max(
                0,
                result.SupportingSourceCount);

        result.ContradictingSourceCount =
            Math.Max(
                0,
                result.ContradictingSourceCount);

        Console.WriteLine(
            "===== FACT VERIFIER SUCCESS =====");

        return result;
    }
}