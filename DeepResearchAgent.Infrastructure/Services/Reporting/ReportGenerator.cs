using System.Text.Json;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Reporting;

public class ReportGenerator : IReportGenerator
{
    private readonly IAIClient _aiClient;

    public ReportGenerator(IAIClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<Report> GenerateAsync(
        Research research,
        IReadOnlyList<Fact> facts,
        CancellationToken cancellationToken = default)
    {
        if (facts.Count == 0)
        {
            throw new InvalidOperationException(
                "Report cannot be generated because no verified facts are available.");
        }

        var prompt = BuildPrompt(research, facts);

        var responseSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                title = new
                {
                    type = "STRING"
                },
                content = new
                {
                    type = "STRING"
                }
            },
            required = new[]
            {
                "title",
                "content"
            }
        };

        var response = await _aiClient.GenerateAsync(
            prompt,
            responseSchema,
            cancellationToken);

        var generatedReport =
            JsonSerializer.Deserialize<GeneratedReportDto>(
                response,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (generatedReport is null)
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid report.");
        }

        if (string.IsNullOrWhiteSpace(generatedReport.Title))
        {
            throw new InvalidOperationException(
                "Generated report title is empty.");
        }

        if (string.IsNullOrWhiteSpace(generatedReport.Content))
        {
            throw new InvalidOperationException(
                "Generated report content is empty.");
        }

        return new Report
        {
            Id = Guid.NewGuid(),
            ResearchId = research.Id,
            Title = generatedReport.Title.Trim(),
            Content = generatedReport.Content.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string BuildPrompt(
        Research research,
        IReadOnlyList<Fact> facts)
    {
        var factsText = string.Join(
            "\n\n",
            facts.Select((fact, index) =>
                $"""
                FACT {index + 1}

                Claim:
                {fact.Claim}

                Value:
                {fact.Value ?? "N/A"}

                Source:
                {fact.Source.Title ?? "Unknown"}

                URL:
                {fact.Source.Url}

                Verification Status:
                {fact.VerificationStatus}

                Verification Confidence:
                {fact.VerificationConfidence?.ToString("F2") ?? "N/A"}

                Supporting Sources:
                {fact.SupportingSourceCount}

                Contradicting Sources:
                {fact.ContradictingSourceCount}

                Verification Summary:
                {fact.VerificationSummary ?? "N/A"}
                """));

        return $"""
        You are an expert research report writer.

        Research topic:
        {research.Query}

        Your task is to create a clear, objective and well-structured
        research report using ONLY the verified facts provided below.

        IMPORTANT RULES:

        1. Do not invent facts.
        2. Do not introduce information that is not supported by the provided facts.
        3. Do not present uncertain information as certain.
        4. If a fact is partially supported, clearly communicate the uncertainty.
        5. Prefer evidence with higher verification confidence.
        6. Keep the report focused on the research topic.
        7. Explain important findings in a logical order.
        8. Include a conclusion based only on the provided evidence.
        9. Mention limitations when the available evidence is insufficient.
        10. Do not fabricate sources or URLs.

        Create the report with the following structure:

        # Introduction

        Explain what the research investigates.

        # Key Findings

        Present the most important verified findings.

        # Detailed Analysis

        Explain the findings and their implications.

        # Evidence and Reliability

        Discuss the reliability of the findings based on their
        verification status and supporting evidence.

        # Conclusion

        Summarize the main conclusions supported by the evidence.

        # Limitations

        Mention important uncertainties, gaps or limitations in the evidence.

        VERIFIED FACTS:

        {factsText}
        """;
    }
}