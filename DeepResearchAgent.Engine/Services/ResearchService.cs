
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Engine.Services;

public class ResearchService : IResearchService
{
    private readonly IResearchRepository _researchRepository;
    private readonly IResearchPlanner _researchPlanner;
    private readonly IResearchSearcher _researchSearcher;
    private readonly ICrawler _crawler;
    private readonly IResearchAnalyzer _researchAnalyzer;
    private readonly IFactVerifier _factVerifier;
    private readonly IReportGenerator _reportGenerator;

    public ResearchService(
        IResearchRepository researchRepository,
        IResearchPlanner researchPlanner,
        IResearchSearcher researchSearcher,
        ICrawler crawler,
        IResearchAnalyzer researchAnalyzer,
        IFactVerifier factVerifier,
        IReportGenerator reportGenerator)
    {
        _researchRepository = researchRepository;
        _researchPlanner = researchPlanner;
        _researchSearcher = researchSearcher;
        _crawler = crawler;
        _researchAnalyzer = researchAnalyzer;
        _factVerifier = factVerifier;
        _reportGenerator = reportGenerator;
    }

    public async Task<CreateResearchResponse> CreateResearchAsync(
        CreateResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException(
                "Research query boş olamaz.",
                nameof(request.Query));
        }

        var research = new Research
        {
            Id = Guid.NewGuid(),
            Query = request.Query.Trim(),
            Status = ResearchStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _researchRepository.AddAsync(
            research, cancellationToken);

        return new CreateResearchResponse
        {
            Id = research.Id
        };
    }

    public async Task<GetResearchResponse?> GetResearchByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var research = await _researchRepository.GetByIdAsync(
            id, cancellationToken);

        if (research is null)
            return null;

        return new GetResearchResponse
        {
            Id = research.Id,
            Query = research.Query,
            Status = research.Status.ToString(),
            CreatedAt = research.CreatedAt,
            StartedAt = research.StartedAt,
            CompletedAt = research.CompletedAt
        };
    }

    public async Task<GetReportResponse?> GetReportByResearchIdAsync(
        Guid researchId,
        CancellationToken cancellationToken = default)
    {
        var report =
            await _researchRepository.GetReportByResearchIdAsync(
                researchId, cancellationToken);

        if (report is null)
            return null;

        return new GetReportResponse
        {
            Id = report.Id,
            ResearchId = report.ResearchId,
            Title = report.Title,
            Content = report.Content,
            CreatedAt = report.CreatedAt
        };
    }

    public async Task<bool> ProcessPendingResearchAsync(
        CancellationToken cancellationToken = default)
    {
        var research =
            await _researchRepository.GetPendingResearchAsync(
                cancellationToken);

        if (research is null)
            return false;

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("RESEARCH PROCESSING STARTED");
        Console.WriteLine("======================================");
        Console.WriteLine($"Research ID: {research.Id}");
        Console.WriteLine($"Query: {research.Query}");

        try
        {
            research.StartedAt = DateTime.UtcNow;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            // 1. PLANNING
            research.Status = ResearchStatus.Planning;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("PLANNING STARTED");
            Console.WriteLine("======================================");

            var tasks = await _researchPlanner.CreatePlanAsync(
                research, cancellationToken);

            if (tasks is null || tasks.Count == 0)
            {
                throw new InvalidOperationException(
                    "Research planner herhangi bir task oluşturmadı.");
            }

            await _researchRepository.AddTasksAsync(
                tasks, cancellationToken);

            Console.WriteLine(
                $"Planning tamamlandı. Task sayısı: {tasks.Count}");

            // 2. SEARCHING
            research.Status = ResearchStatus.Searching;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("SEARCHING STARTED");
            Console.WriteLine("======================================");

            var allSearchResults = new List<SearchResultDto>();

            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"Search task: {task.Query}");

                try
                {
                    var searchResults =
                        await _researchSearcher.SearchAsync(
                            task.Query, cancellationToken);

                    if (searchResults is not null)
                        allSearchResults.AddRange(searchResults);

                    task.Status = ResearchStatus.Searching;

                    await _researchRepository.UpdateTaskAsync(
                        task, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Search task başarısız: {task.Query}");
                    Console.WriteLine($"Hata: {ex}");

                    task.Status = ResearchStatus.Failed;

                    await _researchRepository.UpdateTaskAsync(
                        task, cancellationToken);
                }
            }

            var distinctSearchResults = allSearchResults
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(
                    x => x.Url.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (distinctSearchResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "Search sonucunda herhangi bir kaynak bulunamadı.");
            }

            Console.WriteLine(
                $"Benzersiz search result: {distinctSearchResults.Count}");

            // 3. CRAWLING
            research.Status = ResearchStatus.Crawling;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("CRAWLING STARTED");
            Console.WriteLine("======================================");

            var sources = new List<Source>();

            foreach (var searchResult in distinctSearchResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"Crawling: {searchResult.Url}");

                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    ResearchId = research.Id,
                    Url = searchResult.Url,
                    Title = searchResult.Title,
                    Depth = 0,
                    RelevanceScore = 0,
                    CrawlSucceeded = false,
                    CrawledAt = DateTime.UtcNow
                };

                try
                {
                    var crawlResult = await _crawler.CrawlAsync(
                        searchResult.Url, cancellationToken);

                    source.Content = crawlResult;
                    source.CrawlSucceeded =
                        !string.IsNullOrWhiteSpace(crawlResult);

                    Console.WriteLine(
                        $"Crawl başarılı: {source.CrawlSucceeded}");
                    Console.WriteLine(
                        $"İçerik uzunluğu: {crawlResult?.Length ?? 0}");
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine(
                        $"Crawl timeout/cancellation: {searchResult.Url}");

                    source.CrawlSucceeded = false;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Crawl başarısız: {searchResult.Url}");
                    Console.WriteLine($"Hata: {ex}");

                    source.CrawlSucceeded = false;
                }

                sources.Add(source);
            }

            if (!sources.Any(x => x.CrawlSucceeded))
            {
                throw new InvalidOperationException(
                    "Hiçbir source başarıyla crawl edilemedi.");
            }

            await _researchRepository.AddSourcesAsync(
                sources, cancellationToken);

            Console.WriteLine($"Toplam source: {sources.Count}");
            Console.WriteLine(
                $"Başarılı crawl: {sources.Count(x => x.CrawlSucceeded)}");

            // 4. ANALYZING
            research.Status = ResearchStatus.Analyzing;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("ANALYZING STARTED");
            Console.WriteLine("======================================");

            // Değişiklik: Tüm başarılı kaynakları analiz et.
            // Daha fazla kaynak analiz etmek işlem maliyetini artırabilir.
            var successfulSources = sources
                .Where(x =>
                    x.CrawlSucceeded &&
                    !string.IsNullOrWhiteSpace(x.Content))
                .ToList();

            Console.WriteLine(
                $"Analyze edilecek source sayısı: {successfulSources.Count}");

            var allFacts = new List<Fact>();

            foreach (var source in successfulSources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine();
                Console.WriteLine($"Analyzing source: {source.Title}");
                Console.WriteLine($"URL: {source.Url}");

                try
                {
                    var facts = await _researchAnalyzer.AnalyzeAsync(
                        research, source, cancellationToken);

                    if (facts is null || facts.Count == 0)
                    {
                        Console.WriteLine(
                            "Bu source için fact bulunamadı.");
                        continue;
                    }

                    foreach (var fact in facts)
                    {
                        fact.Id = fact.Id == Guid.Empty
                            ? Guid.NewGuid()
                            : fact.Id;

                        fact.SourceId = source.Id;
                    }

                    allFacts.AddRange(facts);

                    Console.WriteLine(
                        $"Source'tan çıkarılan fact sayısı: {facts.Count}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Source analysis başarısız.");
                    Console.WriteLine($"Source: {source.Url}");
                    Console.WriteLine($"Hata: {ex}");
                }
            }

            allFacts = allFacts
                .Where(x => !string.IsNullOrWhiteSpace(x.Claim))
                .GroupBy(
                    x => $"{x.SourceId}:{x.Claim.Trim()}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (allFacts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Analyze aşamasında herhangi bir fact çıkarılamadı.");
            }

            await _researchRepository.AddFactsAsync(
                allFacts, cancellationToken);

            Console.WriteLine($"Toplam fact sayısı: {allFacts.Count}");

            // 5. VERIFICATION
            research.Status = ResearchStatus.Verifying;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("FACT VERIFICATION STARTED");
            Console.WriteLine("======================================");

            IReadOnlyList<BatchFactVerificationResultDto>
                verificationResults;

            try
            {
                verificationResults =
                    await _factVerifier.VerifyBatchAsync(
                        allFacts, allFacts, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Batch fact verification sırasında hata oluştu.");
                Console.WriteLine($"Hata: {ex}");

                verificationResults = allFacts
                    .Select(fact => new BatchFactVerificationResultDto
                    {
                        FactId = fact.Id,
                        Status =
                            FactVerificationStatus.InsufficientEvidence,
                        VerificationConfidence = 0,
                        SupportingSourceCount = 0,
                        ContradictingSourceCount = 0,
                        SupportingSourceIds = [],
                        ContradictingSourceIds = [],
                        Summary =
                            "Fact verification servisi kullanılamadı."
                    })
                    .ToList();
            }

            // Doğrulama sonuçlarını, kaynak ID'leri dahil, ayrıntılı logla.
            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("VERIFICATION RESULT DIAGNOSTICS");
            Console.WriteLine("======================================");

            foreach (var result in verificationResults)
            {
                Console.WriteLine($"FactId: {result.FactId}");
                Console.WriteLine($"Status: {result.Status}");
                Console.WriteLine(
                    $"Confidence: {result.VerificationConfidence}");
                Console.WriteLine(
                    $"Supporting count: {result.SupportingSourceCount}");
                Console.WriteLine(
                    $"Contradicting count: {result.ContradictingSourceCount}");
                Console.WriteLine(
                    $"Supporting IDs: {string.Join(", ", result.SupportingSourceIds ?? [])}");
                Console.WriteLine(
                    $"Contradicting IDs: {string.Join(", ", result.ContradictingSourceIds ?? [])}");
                Console.WriteLine($"Summary: {result.Summary}");
                Console.WriteLine("--------------------------------------");
            }

            var resultsByFactId = verificationResults
                .Where(x => x.FactId != Guid.Empty)
                .GroupBy(x => x.FactId)
                .ToDictionary(g => g.Key, g => g.First());

            var validSourceIds = sources
                .Where(x => x.CrawlSucceeded)
                .Select(x => x.Id)
                .ToHashSet();

            var citations = new List<Citation>();

            foreach (var fact in allFacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!resultsByFactId.TryGetValue(
                    fact.Id, out var verification))
                {
                    fact.VerificationStatus =
                        FactVerificationStatus.InsufficientEvidence;
                    fact.VerificationConfidence = 0;
                    fact.SupportingSourceCount = 0;
                    fact.ContradictingSourceCount = 0;
                    fact.VerificationSummary =
                        "Verification sonucu bulunamadı.";
                    fact.VerifiedAt = DateTime.UtcNow;

                    Console.WriteLine(
                        $"Verification sonucu eksik: FactId={fact.Id}");

                    continue;
                }

                fact.VerificationConfidence =
                    double.IsFinite(verification.VerificationConfidence)
                        ? Math.Clamp(
                            verification.VerificationConfidence, 0.0, 1.0)
                        : 0.0;

                fact.VerificationStatus = verification.Status;
                fact.VerificationSummary =
                    verification.Summary ?? string.Empty;
                fact.VerifiedAt = DateTime.UtcNow;

                var supportingSourceIds =
                    (verification.SupportingSourceIds ?? [])
                    .Where(id => id != Guid.Empty)
                    .Where(validSourceIds.Contains)
                    .Where(id => id != fact.SourceId)
                    .ToHashSet();

                var contradictingSourceIds =
                    (verification.ContradictingSourceIds ?? [])
                    .Where(id => id != Guid.Empty)
                    .Where(validSourceIds.Contains)
                    .Where(id => id != fact.SourceId)
                    .Where(id => !supportingSourceIds.Contains(id))
                    .ToHashSet();

                fact.SupportingSourceCount = supportingSourceIds.Count;
                fact.ContradictingSourceCount =
                    contradictingSourceIds.Count;

                foreach (var sourceId in supportingSourceIds)
                {
                    citations.Add(new Citation
                    {
                        Id = Guid.NewGuid(),
                        FactId = fact.Id,
                        SourceId = sourceId,
                        SupportsFact = true
                    });
                }

                foreach (var sourceId in contradictingSourceIds)
                {
                    citations.Add(new Citation
                    {
                        Id = Guid.NewGuid(),
                        FactId = fact.Id,
                        SourceId = sourceId,
                        SupportsFact = false
                    });
                }

                Console.WriteLine();
                Console.WriteLine("--------------------------------------");
                Console.WriteLine("FACT VERIFICATION RESULT");
                Console.WriteLine($"Fact ID: {fact.Id}");
                Console.WriteLine($"Claim: {fact.Claim}");
                Console.WriteLine($"Status: {fact.VerificationStatus}");
                Console.WriteLine(
                    $"Confidence: {fact.VerificationConfidence}");
                Console.WriteLine(
                    $"Supporting Sources: {fact.SupportingSourceCount}");
                Console.WriteLine(
                    $"Contradicting Sources: {fact.ContradictingSourceCount}");
                Console.WriteLine(
                    $"Summary: {fact.VerificationSummary}");
                Console.WriteLine("--------------------------------------");
            }

            citations = citations
                .GroupBy(c => new
                {
                    c.FactId,
                    c.SourceId,
                    c.SupportsFact
                })
                .Select(g => g.First())
                .ToList();

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("CITATION PERSISTENCE");
            Console.WriteLine("======================================");
            Console.WriteLine(
                $"Verification results: {verificationResults.Count}");
            Console.WriteLine($"Facts to update: {allFacts.Count}");
            Console.WriteLine($"Valid source IDs: {validSourceIds.Count}");
            Console.WriteLine($"Citations prepared: {citations.Count}");

            foreach (var citation in citations)
            {
                Console.WriteLine(
                    $"Citation: FactId={citation.FactId}, " +
                    $"SourceId={citation.SourceId}, " +
                    $"SupportsFact={citation.SupportsFact}");
            }

            await _researchRepository.UpdateFactsAsync(
                allFacts, cancellationToken);

            if (citations.Count > 0)
            {
                await _researchRepository.AddCitationsAsync(
                    citations, cancellationToken);
            }
            else
            {
                Console.WriteLine(
                    "UYARI: Hiç citation oluşturulmadı. " +
                    "FactVerifier kanıt eşleştirmesini kontrol et.");
            }

            Console.WriteLine("Verification tamamlandı.");
            Console.WriteLine($"Toplam fact: {allFacts.Count}");
            Console.WriteLine($"Toplam citation: {citations.Count}");

            Console.WriteLine(
                $"Supported: {allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.Supported)}");

            Console.WriteLine(
                $"PartiallySupported: {allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.PartiallySupported)}");

            Console.WriteLine(
                $"Contradicted: {allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.Contradicted)}");

            Console.WriteLine(
                $"InsufficientEvidence: {allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.InsufficientEvidence)}");

            // 6. REPORT GENERATION
            research.Status = ResearchStatus.GeneratingReport;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("REPORT GENERATION STARTED");
            Console.WriteLine("======================================");

            Console.WriteLine("FACT STATUS DISTRIBUTION");

            foreach (var group in allFacts.GroupBy(
                x => x.VerificationStatus))
            {
                Console.WriteLine($"{group.Key}: {group.Count()}");
            }

            var reportableFacts = allFacts
                .Where(x =>
                    x.VerificationStatus ==
                        FactVerificationStatus.Supported ||
                    x.VerificationStatus ==
                        FactVerificationStatus.PartiallySupported)
                .ToList();

            Console.WriteLine(
                $"Toplam analiz edilen fact: {allFacts.Count}");
            Console.WriteLine(
                $"Rapor için kullanılacak fact sayısı: {reportableFacts.Count}");

            foreach (var fact in allFacts.Except(reportableFacts))
            {
                Console.WriteLine(
                    $"RAPORDAN ELENDİ | " +
                    $"FactId={fact.Id} | " +
                    $"Status={fact.VerificationStatus} | " +
                    $"Confidence={fact.VerificationConfidence} | " +
                    $"Supporting={fact.SupportingSourceCount} | " +
                    $"Contradicting={fact.ContradictingSourceCount} | " +
                    $"Claim={fact.Claim}");
            }

            foreach (var fact in reportableFacts)
            {
                Console.WriteLine(
                    $"RAPORA ALINDI | " +
                    $"FactId={fact.Id} | " +
                    $"Status={fact.VerificationStatus} | " +
                    $"Confidence={fact.VerificationConfidence} | " +
                    $"Claim={fact.Claim}");
            }

            if (reportableFacts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Rapor oluşturmak için doğrulanmış fact bulunamadı.");
            }

            var report = await _reportGenerator.GenerateAsync(
                research, reportableFacts, cancellationToken);

            await _researchRepository.AddReportAsync(
                report, cancellationToken);

            Console.WriteLine("Report başarıyla oluşturuldu.");

            // 7. COMPLETED
            research.Status = ResearchStatus.Completed;
            research.CompletedAt = DateTime.UtcNow;

            await _researchRepository.UpdateAsync(
                research, cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("RESEARCH COMPLETED");
            Console.WriteLine("======================================");

            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                "Research cancellation nedeniyle durduruldu.");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("RESEARCH FAILED");
            Console.WriteLine("======================================");
            Console.WriteLine($"Exception: {ex}");

            try
            {
                research.Status = ResearchStatus.Failed;
                research.CompletedAt = DateTime.UtcNow;

                await _researchRepository.UpdateAsync(
                    research, CancellationToken.None);
            }
            catch (Exception updateException)
            {
                Console.WriteLine(
                    "Research Failed statusu database'e kaydedilemedi.");
                Console.WriteLine($"Update error: {updateException}");
            }

            return true;
        }
    }
}
