
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Verification;

public class FactVerifier : IFactVerifier
{
    private readonly IAIClient _aiClient;

    private const int BatchSize = 5;
    private const int MaxEvidencePerFact = 5;
    private const double MinimumSimilarity = 0.05;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly HashSet<string> StopWords = new(
        new[]
        {
            "ve", "veya", "ile", "bir", "bu", "şu", "o",
            "için", "gibi", "olan", "olarak", "daha", "çok",
            "kadar", "sonra", "önce", "arasında", "tarafından",
            "ise", "ama", "ancak", "hem", "her", "bazı",
            "the", "a", "an", "and", "or", "of", "to", "in",
            "on", "for", "with", "by", "is", "are", "was",
            "were", "be", "been", "being", "that", "this",
            "these", "those", "it", "its", "as", "at", "from"
        },
        StringComparer.OrdinalIgnoreCase);

    public FactVerifier(IAIClient aiClient)
    {
        _aiClient = aiClient
            ?? throw new ArgumentNullException(nameof(aiClient));
    }

    // =========================================================
    // SINGLE FACT VERIFICATION
    // =========================================================

    public async Task<FactVerificationResultDto> VerifyAsync(
        Fact fact,
        IReadOnlyList<Fact> relatedFacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(relatedFacts);

        var results = await VerifyBatchAsync(
            new[] { fact },
            relatedFacts,
            cancellationToken);

        var result = results.FirstOrDefault(
            x => x.FactId == fact.Id);

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Fact {fact.Id} için doğrulama sonucu oluşturulamadı.");
        }

        return new FactVerificationResultDto
        {
            Status = result.Status,
            VerificationConfidence = result.VerificationConfidence,
            SupportingSourceCount = result.SupportingSourceCount,
            ContradictingSourceCount = result.ContradictingSourceCount,
            Summary = result.Summary
        };
    }

    // =========================================================
    // BATCH VERIFICATION
    // =========================================================

    public async Task<IReadOnlyList<BatchFactVerificationResultDto>>
        VerifyBatchAsync(
            IReadOnlyList<Fact> facts,
            IReadOnlyList<Fact> allFacts,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(allFacts);

        if (facts.Count == 0)
        {
            return [];
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT VERIFICATION STARTED");
        Console.WriteLine("======================================");
        Console.WriteLine($"Facts to verify: {facts.Count}");
        Console.WriteLine($"Available facts: {allFacts.Count}");
        Console.WriteLine($"Batch size: {BatchSize}");

        var results = new List<BatchFactVerificationResultDto>();
        var batches = facts.Chunk(BatchSize).ToList();

        Console.WriteLine($"Total batches: {batches.Count}");

        for (var index = 0; index < batches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = batches[index];

            Console.WriteLine();
            Console.WriteLine(
                $"Verification batch {index + 1}/{batches.Count}");

            try
            {
                var batchResults = await VerifySingleBatchAsync(
                    batch,
                    allFacts,
                    cancellationToken);

                results.AddRange(batchResults);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Batch verification failed. " +
                    "The failure will not be converted into InsufficientEvidence.");

                Console.WriteLine(ex);

                // ÖNEMLİ:
                // API kota hatası, ağ hatası veya AI yanıtı işleme hatası
                // gerçek bir kanıt yetersizliği değildir.
                // Hatanın ResearchService katmanına ulaşmasına izin ver.
                throw;
            }
        }

        var resultsByFactId = results
            .GroupBy(x => x.FactId)
            .ToDictionary(g => g.Key, g => g.First());

        var orderedResults = facts
            .Select(fact =>
            {
                if (resultsByFactId.TryGetValue(
                        fact.Id, out var result))
                {
                    return result;
                }

                // Bu durum API çağrısı başarılı olduğu hâlde
                // sonuç listesinde ilgili fact yoksa oluşur.
                return CreateBatchInsufficientResult(
                    fact.Id,
                    "Başarılı doğrulama işlemi sonucunda bu fact için sonuç bulunamadı.");
            })
            .ToList();

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("BATCH FACT VERIFICATION COMPLETED");
        Console.WriteLine("======================================");
        Console.WriteLine($"Results: {orderedResults.Count}");

        foreach (var group in orderedResults.GroupBy(x => x.Status))
        {
            Console.WriteLine($"{group.Key}: {group.Count()}");
        }

        return orderedResults;
    }

    // =========================================================
    // VERIFY ONE BATCH
    // =========================================================

    private async Task<List<BatchFactVerificationResultDto>>
        VerifySingleBatchAsync(
            IReadOnlyList<Fact> batch,
            IReadOnlyList<Fact> allFacts,
            CancellationToken cancellationToken)
    {
        var evidenceByFactId =
            new Dictionary<Guid, List<FactEvidence>>();

        foreach (var fact in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var evidence = FindRelatedEvidence(fact, allFacts);

            evidenceByFactId[fact.Id] = evidence;

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine($"Fact ID: {fact.Id}");
            Console.WriteLine($"Claim: {fact.Claim}");
            Console.WriteLine($"Source ID: {fact.SourceId}");
            Console.WriteLine($"Evidence candidates: {evidence.Count}");

            foreach (var item in evidence)
            {
                Console.WriteLine(
                    $"Evidence FactId={item.FactId}, " +
                    $"SourceId={item.SourceId}, " +
                    $"Similarity={item.Similarity:F3}");

                Console.WriteLine($"Evidence claim: {item.Claim}");
            }
        }

        // Hiçbir fact için bağımsız kanıt adayı yoksa
        // bu batch için AI çağrısı yapmaya gerek yoktur.
        if (evidenceByFactId.Values.All(x => x.Count == 0))
        {
            Console.WriteLine(
                "Bu batch içindeki hiçbir fact için " +
                "bağımsız kanıt adayı bulunamadı.");

            return batch
                .Select(fact => CreateBatchInsufficientResult(
                    fact.Id,
                    "Diğer kaynaklardan ilgili bağımsız kanıt adayı bulunamadı."))
                .ToList();
        }

        var prompt = BuildPrompt(batch, evidenceByFactId);
        var schema = BuildResponseSchema();

        // HTTP 429/503 veya başka bir API hatası oluşursa
        // istisna üst katmana iletilir.
        var rawResponse = await _aiClient.GenerateAsync(
            prompt,
            schema,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new InvalidOperationException(
                "AI doğrulama servisi boş yanıt döndürdü.");
        }

        Console.WriteLine();
        Console.WriteLine("AI verification response received.");
        Console.WriteLine($"Response length: {rawResponse.Length}");

        var json = ExtractJsonArray(rawResponse);

        List<AiFactVerificationResult>? aiResults;

        try
        {
            aiResults =
                JsonSerializer.Deserialize<List<AiFactVerificationResult>>(
                    json,
                    JsonOptions);
        }
        catch (JsonException ex)
        {
            Console.WriteLine("AI JSON parse error.");
            Console.WriteLine(ex);

            throw new InvalidOperationException(
                "AI doğrulama yanıtı beklenen JSON dizisi biçiminde değil.",
                ex);
        }

        if (aiResults is null)
        {
            throw new InvalidOperationException(
                "AI doğrulama yanıtı boş bir JSON sonucu üretti.");
        }

        Console.WriteLine($"AI results parsed: {aiResults.Count}");

        var batchIds = batch
            .Select(x => x.Id)
            .ToHashSet();

        var aiResultsById = aiResults
            .Where(x => x.FactId != Guid.Empty)
            .Where(x => batchIds.Contains(x.FactId))
            .GroupBy(x => x.FactId)
            .ToDictionary(g => g.Key, g => g.First());

        var output = new List<BatchFactVerificationResultDto>();

        foreach (var fact in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!aiResultsById.TryGetValue(
                    fact.Id, out var aiResult))
            {
                Console.WriteLine(
                    $"AI result missing for FactId={fact.Id}");

                output.Add(CreateBatchInsufficientResult(
                    fact.Id,
                    "AI yanıtında bu fact için geçerli bir sonuç bulunamadı."));

                continue;
            }

            var evidenceById = evidenceByFactId[fact.Id]
                .GroupBy(x => x.FactId)
                .ToDictionary(g => g.Key, g => g.First());

            var supportingSourceIds = ResolveSourceIds(
                aiResult.SupportingEvidenceFactIds,
                evidenceById,
                fact.SourceId);

            var contradictingSourceIds = ResolveSourceIds(
                aiResult.ContradictingEvidenceFactIds,
                evidenceById,
                fact.SourceId);

            // Aynı kaynak aynı fact için hem destek hem çelişki
            // olarak sayılmasın.
            contradictingSourceIds.RemoveWhere(
                supportingSourceIds.Contains);

            var status = NormalizeStatus(
                aiResult.Status,
                supportingSourceIds.Count,
                contradictingSourceIds.Count);

            var confidence =
                double.IsFinite(aiResult.VerificationConfidence)
                    ? Math.Clamp(
                        aiResult.VerificationConfidence, 0.0, 1.0)
                    : 0.0;

            if (supportingSourceIds.Count == 0 &&
                contradictingSourceIds.Count == 0)
            {
                confidence = 0;
            }

            var summary = string.IsNullOrWhiteSpace(aiResult.Summary)
                ? "AI doğrulama özeti döndürmedi."
                : aiResult.Summary.Trim();

            output.Add(new BatchFactVerificationResultDto
            {
                FactId = fact.Id,
                Status = status,
                VerificationConfidence = confidence,
                SupportingSourceCount = supportingSourceIds.Count,
                ContradictingSourceCount = contradictingSourceIds.Count,
                SupportingSourceIds = supportingSourceIds.ToList(),
                ContradictingSourceIds = contradictingSourceIds.ToList(),
                Summary = summary
            });

            Console.WriteLine();
            Console.WriteLine("FACT VERIFICATION RESULT");
            Console.WriteLine($"Fact ID: {fact.Id}");
            Console.WriteLine($"Claim: {fact.Claim}");
            Console.WriteLine($"AI status: {aiResult.Status}");
            Console.WriteLine($"Final status: {status}");
            Console.WriteLine($"Confidence: {confidence:F3}");
            Console.WriteLine(
                $"Supporting source IDs: {string.Join(", ", supportingSourceIds)}");
            Console.WriteLine(
                $"Contradicting source IDs: {string.Join(", ", contradictingSourceIds)}");
            Console.WriteLine($"Summary: {summary}");
        }

        return output;
    }

    // =========================================================
    // EVIDENCE SELECTION
    // =========================================================

    private static List<FactEvidence> FindRelatedEvidence(
        Fact targetFact,
        IReadOnlyList<Fact> allFacts)
    {
        if (string.IsNullOrWhiteSpace(targetFact.Claim))
        {
            return [];
        }

        var targetTokens = GetImportantWords(targetFact.Claim);

        return allFacts
            .Where(candidate =>
                candidate.Id != targetFact.Id &&
                candidate.SourceId != Guid.Empty &&
                candidate.SourceId != targetFact.SourceId &&
                !string.IsNullOrWhiteSpace(candidate.Claim))
            .Select(candidate =>
            {
                var candidateTokens = GetImportantWords(candidate.Claim);

                var jaccard = CalculateJaccard(
                    targetTokens,
                    candidateTokens);

                var containment = CalculateContainment(
                    targetTokens,
                    candidateTokens);

                var score = Math.Max(jaccard, containment * 0.5);

                return new
                {
                    Fact = candidate,
                    Similarity = score
                };
            })
            .Where(x => x.Similarity >= MinimumSimilarity)
            .GroupBy(x => x.Fact.SourceId)
            .Select(group => group
                .OrderByDescending(x => x.Similarity)
                .First())
            .OrderByDescending(x => x.Similarity)
            .Take(MaxEvidencePerFact)
            .Select(x => new FactEvidence
            {
                FactId = x.Fact.Id,
                SourceId = x.Fact.SourceId,
                Claim = x.Fact.Claim,
                Similarity = x.Similarity
            })
            .ToList();
    }

    private static HashSet<Guid> ResolveSourceIds(
        IEnumerable<Guid>? evidenceFactIds,
        IReadOnlyDictionary<Guid, FactEvidence> availableEvidence,
        Guid targetSourceId)
    {
        var sourceIds = new HashSet<Guid>();

        if (evidenceFactIds is null)
        {
            return sourceIds;
        }

        foreach (var evidenceFactId in evidenceFactIds)
        {
            if (evidenceFactId == Guid.Empty)
            {
                continue;
            }

            // Yalnızca bu fact için AI'ye sağlanan kanıt
            // Fact ID'lerinin kullanılmasına izin ver.
            if (!availableEvidence.TryGetValue(
                    evidenceFactId, out var evidence))
            {
                Console.WriteLine(
                    $"AI returned an unknown evidence FactId: {evidenceFactId}");

                continue;
            }

            if (evidence.SourceId == Guid.Empty ||
                evidence.SourceId == targetSourceId)
            {
                continue;
            }

            sourceIds.Add(evidence.SourceId);
        }

        return sourceIds;
    }

    // =========================================================
    // SIMILARITY
    // =========================================================

    private static double CalculateJaccard(
        HashSet<string> first,
        HashSet<string> second)
    {
        if (first.Count == 0 || second.Count == 0)
        {
            return 0;
        }

        var intersection = first.Intersect(
            second,
            StringComparer.OrdinalIgnoreCase).Count();

        var union = first.Union(
            second,
            StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    private static double CalculateContainment(
        HashSet<string> first,
        HashSet<string> second)
    {
        if (first.Count == 0 || second.Count == 0)
        {
            return 0;
        }

        var smaller = first.Count <= second.Count ? first : second;
        var larger = first.Count <= second.Count ? second : first;

        var intersection = smaller.Intersect(
            larger,
            StringComparer.OrdinalIgnoreCase).Count();

        return (double)intersection / smaller.Count;
    }

    private static HashSet<string> GetImportantWords(string text)
    {
        var normalized = text
            .ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

        var words = normalized.Split(
            new[]
            {
                ' ', '\t', '\r', '\n',
                '.', ',', ';', ':', '!',
                '?', '(', ')', '[', ']',
                '{', '}', '"', '\'', '/',
                '\\', '-', '_', '+', '=', '|',
                '%', '&', '*', '<', '>'
            },
            StringSplitOptions.RemoveEmptyEntries);

        return words
            .Where(word =>
                word.Length >= 3 &&
                !StopWords.Contains(word))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // =========================================================
    // STATUS NORMALIZATION
    // =========================================================

    private static FactVerificationStatus NormalizeStatus(
        FactVerificationStatus requestedStatus,
        int supportingSourceCount,
        int contradictingSourceCount)
    {
        if (supportingSourceCount == 0 &&
            contradictingSourceCount == 0)
        {
            return FactVerificationStatus.InsufficientEvidence;
        }

        if (supportingSourceCount > 0 &&
            contradictingSourceCount > 0)
        {
            return FactVerificationStatus.PartiallySupported;
        }

        if (supportingSourceCount == 0 &&
            contradictingSourceCount > 0)
        {
            return FactVerificationStatus.Contradicted;
        }

        return requestedStatus ==
               FactVerificationStatus.Contradicted
            ? FactVerificationStatus.PartiallySupported
            : requestedStatus;
    }

    // =========================================================
    // JSON RESPONSE HANDLING
    // =========================================================

    private static string ExtractJsonArray(string response)
    {
        var json = response.Trim();

        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = json.IndexOf('\n');

            if (firstNewLine >= 0)
            {
                json = json[(firstNewLine + 1)..];
            }

            var lastFence = json.LastIndexOf(
                "```", StringComparison.Ordinal);

            if (lastFence >= 0)
            {
                json = json[..lastFence];
            }

            json = json.Trim();
        }

        var start = json.IndexOf('[');
        var end = json.LastIndexOf(']');

        if (start < 0 || end < start)
        {
            throw new InvalidOperationException(
                "AI yanıtında JSON dizisi bulunamadı.");
        }

        return json[start..(end + 1)];
    }

    // =========================================================
    // PROMPT
    // =========================================================

    private static string BuildPrompt(
        IReadOnlyList<Fact> batch,
        IReadOnlyDictionary<Guid, List<FactEvidence>> evidenceByFactId)
    {
        var sections = new List<string>();

        foreach (var fact in batch)
        {
            var evidence = evidenceByFactId[fact.Id];

            var evidenceText = evidence.Count == 0
                ? "No independent evidence candidates were found."
                : string.Join(
                    "\n\n",
                    evidence.Select(item =>
                        $"""
                        EvidenceFactId: {item.FactId}
                        SourceId: {item.SourceId}
                        Similarity: {item.Similarity:F3}
                        Claim: {item.Claim}
                        """));

            sections.Add(
                $"""
                TARGET FACT
                FactId: {fact.Id}
                SourceId: {fact.SourceId}
                Claim: {fact.Claim}

                INDEPENDENT EVIDENCE CANDIDATES:
                {evidenceText}
                """);
        }

        return
            """
            You are a conservative fact verification component.

            Verify each target claim using only the evidence candidates
            explicitly supplied for that target.

            Rules:
            - Never invent facts, sources, or identifiers.
            - Do not use outside knowledge as evidence.
            - EvidenceFactId must exactly match an ID supplied for that target.
            - Do not use the target fact itself as evidence.
            - Evidence must support or contradict the meaning of the claim;
              shared keywords alone are not sufficient.
            - Do not treat an unrelated claim as supporting evidence.
            - Return a supporting evidence ID only if that evidence supports
              the target claim.
            - Return a contradicting evidence ID only if it directly
              contradicts the target claim.
            - If evidence is insufficient, return InsufficientEvidence.
            - Do not infer precise percentages, dates, or attributions unless
              the supplied evidence actually supports them.
            - Confidence must be a number between 0 and 1.
            - Return exactly one result for every target FactId.
            - Return JSON only, without Markdown fences.

            Allowed statuses:
            Supported, PartiallySupported, Contradicted, InsufficientEvidence.
            """.Trim()
            + "\n\nTARGETS AND EVIDENCE:\n\n"
            + string.Join("\n\n", sections);
    }

    // =========================================================
    // RESPONSE SCHEMA
    // =========================================================

    private static object BuildResponseSchema()
    {
        return new
        {
            type = "ARRAY",
            items = new
            {
                type = "OBJECT",
                properties = new
                {
                    factId = new { type = "STRING" },
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
                    verificationConfidence = new { type = "NUMBER" },
                    supportingEvidenceFactIds = new
                    {
                        type = "ARRAY",
                        items = new { type = "STRING" }
                    },
                    contradictingEvidenceFactIds = new
                    {
                        type = "ARRAY",
                        items = new { type = "STRING" }
                    },
                    summary = new { type = "STRING" }
                },
                required = new[]
                {
                    "factId",
                    "status",
                    "verificationConfidence",
                    "supportingEvidenceFactIds",
                    "contradictingEvidenceFactIds",
                    "summary"
                }
            }
        };
    }

    // =========================================================
    // FALLBACK RESULT
    // =========================================================

    private static BatchFactVerificationResultDto
        CreateBatchInsufficientResult(Guid factId, string summary)
    {
        return new BatchFactVerificationResultDto
        {
            FactId = factId,
            Status = FactVerificationStatus.InsufficientEvidence,
            VerificationConfidence = 0,
            SupportingSourceCount = 0,
            ContradictingSourceCount = 0,
            SupportingSourceIds = [],
            ContradictingSourceIds = [],
            Summary = summary
        };
    }

    // =========================================================
    // INTERNAL MODELS
    // =========================================================

    private sealed class AiFactVerificationResult
    {
        [JsonPropertyName("factId")]
        public Guid FactId { get; set; }

        [JsonPropertyName("status")]
        public FactVerificationStatus Status { get; set; } =
            FactVerificationStatus.InsufficientEvidence;

        [JsonPropertyName("verificationConfidence")]
        public double VerificationConfidence { get; set; }

        [JsonPropertyName("supportingEvidenceFactIds")]
        public List<Guid> SupportingEvidenceFactIds { get; set; } = [];

        [JsonPropertyName("contradictingEvidenceFactIds")]
        public List<Guid> ContradictingEvidenceFactIds { get; set; } = [];

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class FactEvidence
    {
        public Guid FactId { get; init; }

        public Guid SourceId { get; init; }

        public string Claim { get; init; } = string.Empty;

        public double Similarity { get; init; }
    }
}
