
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var sourceCatalog = BuildSourceCatalog(facts);
        var prompt = BuildPrompt(research, facts, sourceCatalog);

        var responseSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                title = new { type = "STRING" },
                content = new { type = "STRING" }
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

        var content = RemoveUnknownCitations(
            generatedReport.Content.Trim(),
            sourceCatalog);

        content = AppendSourceReferences(content, sourceCatalog);

        return new Report
        {
            Id = Guid.NewGuid(),
            ResearchId = research.Id,
            Title = generatedReport.Title.Trim(),
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Dictionary<Guid, SourceReference> BuildSourceCatalog(
        IReadOnlyList<Fact> facts)
    {
        var catalog = new Dictionary<Guid, SourceReference>();
        var labelsByKey = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var fact in facts)
        {
            var source = fact.Source;

            var title = source?.Title?.Trim() ?? "Unknown source";
            var url = source?.Url?.Trim() ?? string.Empty;

            // Prefer URL to identify a source.
            // If no URL exists, use its title.
            var key = !string.IsNullOrWhiteSpace(url)
                ? url
                : !string.IsNullOrWhiteSpace(title)
                    ? title
                    : $"unknown-{fact.Id:N}";

            if (!labelsByKey.TryGetValue(key, out var label))
            {
                label = $"S{labelsByKey.Count + 1}";
                labelsByKey[key] = label;
            }

            catalog[fact.Id] = new SourceReference(
                label,
                title,
                url);
        }

        return catalog;
    }

    private static string BuildPrompt(
        Research research,
        IReadOnlyList<Fact> facts,
        IReadOnlyDictionary<Guid, SourceReference> sourceCatalog)
    {
        var factsText = string.Join(
            "\n\n",
            facts.Select((fact, index) =>
            {
                var source = sourceCatalog[fact.Id];

                return $"""
                    FACT F{index + 1}

                    Fact ID:
                    {fact.Id}

                    Claim:
                    {fact.Claim}

                    Value:
                    {fact.Value ?? "N/A"}

                    Direct Source Reference:
                    [{source.Label}]

                    Source Title:
                    {source.Title}

                    Source URL:
                    {source.Url}

                    Verification Status:
                    {fact.VerificationStatus}

                    Verification Confidence:
                    {fact.VerificationConfidence?.ToString("F2") ?? "N/A"}

                    Supporting Source Count:
                    {fact.SupportingSourceCount}

                    Contradicting Source Count:
                    {fact.ContradictingSourceCount}

                    Verification Summary:
                    {fact.VerificationSummary ?? "N/A"}
                    """;
            }));

        var sourcesText = string.Join(
            "\n",
            sourceCatalog.Values
                .GroupBy(source => source.Label)
                .Select(group => group.First())
                .OrderBy(source =>
                    int.Parse(source.Label.Substring(1)))
                .Select(source =>
                    $"[{source.Label}] {source.Title} - " +
                    (string.IsNullOrWhiteSpace(source.Url)
                        ? "URL unavailable"
                        : source.Url)));

        return $"""
            You are an expert research report writer.

            Research topic:
            {research.Query}

            Write a detailed, objective and evidence-based research report
            using only the facts and source information provided below.

            STRICT EVIDENCE RULES:

            1. Do not invent facts, statistics, quotations, URLs or sources.
            2. Do not introduce claims that are not supported by the provided facts.
            3. Add an inline citation such as [S1] to every important factual claim.
            4. Use only source labels explicitly associated with the relevant facts.
            5. Never invent a source label.
            6. A source being listed does not automatically prove every claim.
            7. Do not describe a fact's direct source as multiple independent sources.
            8. Supporting Source Count is a count, not a list of source identities.
            9. Do not infer the identities of supporting sources from that count.
            10. Distinguish Supported from PartiallySupported facts.
            11. Do not describe partially supported claims as established facts.
            12. Preserve the original meaning of numerical values and dates.
            13. If evidence is insufficient, explain the limitation instead
                of filling the gap with assumptions.
            14. Prefer relevant facts with higher verification confidence.
            15. Do not claim that a source proves something unless the provided
                fact and verification information justify that conclusion.

            REPORT STRUCTURE:

            # Introduction
            Explain the research question and scope.

            # Key Findings
            Summarize the most important findings with inline citations.

            # Detailed Analysis
            Explain each major finding, its evidence and its implications.
            Include specific examples only when provided in the facts.

            # Evidence and Reliability
            Discuss verification status, confidence and evidence limitations.
            Do not imply that a confidence score is a statistical probability.

            # Conclusion
            Summarize only conclusions supported by the evidence.

            # Limitations
            Explain gaps, uncertainty, conflicting evidence and unsupported claims.

            Do not generate a Sources or References section.
            The application will append the source list automatically.

            VERIFIED FACTS:

            {factsText}

            AVAILABLE SOURCE CATALOG:

            {sourcesText}
            """;
    }

    private static string RemoveUnknownCitations(
        string content,
        IReadOnlyDictionary<Guid, SourceReference> sourceCatalog)
    {
        var validLabels = sourceCatalog.Values
            .Select(source => source.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Regex.Replace(
            content,
            @"\[S\d+\]",
            match => validLabels.Contains(match.Value[1..^1])
                ? match.Value
                : string.Empty);
    }

    private static string AppendSourceReferences(
        string content,
        IReadOnlyDictionary<Guid, SourceReference> sourceCatalog)
    {
        var sources = sourceCatalog.Values
            .GroupBy(source => source.Label)
            .Select(group => group.First())
            .OrderBy(source =>
                int.Parse(source.Label.Substring(1)))
            .ToList();

        var references = string.Join(
            "\n",
            sources.Select(source =>
                string.IsNullOrWhiteSpace(source.Url)
                    ? $"- [{source.Label}] {source.Title} (URL unavailable)"
                    : $"- [{source.Label}] [{source.Title}]({source.Url})"));

        return $"""
            {content}

            ## Sources

            {references}
            """;
    }

    private sealed record SourceReference(
        string Label,
        string Title,
        string Url);
}
