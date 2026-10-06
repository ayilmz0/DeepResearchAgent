using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Engine.Services.Analysis;

public class FactRelevanceAnalyzer : IFactRelevanceAnalyzer
{
    private readonly IAIClient _aiClient;

    public FactRelevanceAnalyzer(IAIClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<FactRelevanceResultDto> AnalyzeAsync(
        Research research,
        Fact fact,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
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

        var responseSchema = new
        {
            type = "OBJECT",

            properties = new
            {
                isRelevant = new
                {
                    type = "BOOLEAN"
                },

                confidence = new
                {
                    type = "NUMBER"
                },

                reason = new
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
}