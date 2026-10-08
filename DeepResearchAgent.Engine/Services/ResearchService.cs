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
            research,
            cancellationToken);

        return new CreateResearchResponse
        {
            Id = research.Id
        };
    }

    public async Task<GetResearchResponse?> GetResearchByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var research =
            await _researchRepository.GetByIdAsync(
                id,
                cancellationToken);

        if (research is null)
        {
            return null;
        }

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
                researchId,
                cancellationToken);

        if (report is null)
        {
            return null;
        }

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
        {
            return false;
        }

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
                research,
                cancellationToken);

            // =====================================================
            // 1. PLANNING
            // =====================================================

            research.Status = ResearchStatus.Planning;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("PLANNING STARTED");
            Console.WriteLine("======================================");

            var tasks =
                await _researchPlanner.CreatePlanAsync(
                    research,
                    cancellationToken);

            if (tasks is null || tasks.Count == 0)
            {
                throw new InvalidOperationException(
                    "Research planner herhangi bir task oluşturmadı.");
            }

            await _researchRepository.AddTasksAsync(
                tasks,
                cancellationToken);

            Console.WriteLine(
                $"Planning tamamlandı. Task sayısı: {tasks.Count}");

            // =====================================================
            // 2. SEARCHING
            // =====================================================

            research.Status = ResearchStatus.Searching;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("SEARCHING STARTED");
            Console.WriteLine("======================================");

            var allSearchResults =
                new List<SearchResultDto>();

            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine();
                Console.WriteLine(
                    $"Search task: {task.Query}");

                try
                {
                    var searchResults =
                        await _researchSearcher.SearchAsync(
                            task.Query,
                            cancellationToken);

                    if (searchResults is not null)
                    {
                        allSearchResults.AddRange(searchResults);
                    }

                    task.Status = ResearchStatus.Searching;

                    await _researchRepository.UpdateTaskAsync(
                        task,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Search task başarısız: {task.Query}");

                    Console.WriteLine(
                        $"Hata: {ex.Message}");

                    task.Status = ResearchStatus.Failed;

                    await _researchRepository.UpdateTaskAsync(
                        task,
                        cancellationToken);
                }
            }

            if (allSearchResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "Search sonucunda herhangi bir kaynak bulunamadı.");
            }

            Console.WriteLine(
                $"Toplam search result: {allSearchResults.Count}");

            // =====================================================
            // 3. CRAWLING
            // =====================================================

            research.Status = ResearchStatus.Crawling;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("CRAWLING STARTED");
            Console.WriteLine("======================================");

            var sources = new List<Source>();

            var processedUrls =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var searchResult in allSearchResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(searchResult.Url))
                {
                    continue;
                }

                if (!processedUrls.Add(searchResult.Url))
                {
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"Crawling: {searchResult.Url}");

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
                    var crawlResult =
                        await _crawler.CrawlAsync(
                            searchResult.Url,
                            cancellationToken);

                    source.Content = crawlResult;

                    source.CrawlSucceeded =
                        !string.IsNullOrWhiteSpace(crawlResult);

                    Console.WriteLine(
                        $"Crawl başarılı: {source.CrawlSucceeded}");
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        $"Crawl timeout/cancellation: {searchResult.Url}");

                    Console.WriteLine(
                        "Bu source atlanıyor, research devam ediyor.");

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

                    Console.WriteLine(
                        $"Hata: {ex.Message}");

                    source.CrawlSucceeded = false;
                }

                sources.Add(source);
            }

            if (sources.Count == 0)
            {
                throw new InvalidOperationException(
                    "Herhangi bir source oluşturulamadı.");
            }

            await _researchRepository.AddSourcesAsync(
                sources,
                cancellationToken);

            Console.WriteLine(
                $"Toplam source: {sources.Count}");

            Console.WriteLine(
                $"Başarılı crawl: " +
                $"{sources.Count(x => x.CrawlSucceeded)}");

            // =====================================================
            // 4. ANALYZING
            // =====================================================

            research.Status = ResearchStatus.Analyzing;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("ANALYZING STARTED");
            Console.WriteLine("======================================");

            var successfulSources =
                sources
                    .Where(x =>
                        x.CrawlSucceeded &&
                        !string.IsNullOrWhiteSpace(x.Content))
                    .Take(5)
                    .ToList();

            Console.WriteLine(
                $"Analyze edilecek source sayısı: " +
                $"{successfulSources.Count}");

            var allFacts = new List<Fact>();

            foreach (var source in successfulSources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine();
                Console.WriteLine(
                    $"Analyzing source: {source.Title}");

                try
                {
                    var facts =
                        await _researchAnalyzer.AnalyzeAsync(
                            research,
                            source,
                            cancellationToken);

                    if (facts is null || facts.Count == 0)
                    {
                        Console.WriteLine(
                            "Bu source için fact bulunamadı.");

                        continue;
                    }

                    allFacts.AddRange(facts);

                    Console.WriteLine(
                        $"Source'tan çıkarılan fact sayısı: " +
                        $"{facts.Count}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "Source analysis başarısız.");

                    Console.WriteLine(
                        $"Source: {source.Url}");

                    Console.WriteLine(
                        $"Hata: {ex.Message}");
                }
            }

            if (allFacts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Analyze aşamasında herhangi bir fact çıkarılamadı.");
            }

            await _researchRepository.AddFactsAsync(
                allFacts,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine(
                $"Toplam fact sayısı: {allFacts.Count}");

            // =====================================================
            // 5. VERIFICATION
            // =====================================================

            research.Status = ResearchStatus.Verifying;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

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
                        allFacts,
                        allFacts,
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Batch fact verification sırasında " +
                    "beklenmeyen hata oluştu.");

                Console.WriteLine(
                    $"Hata: {ex}");

                verificationResults =
                    allFacts
                        .Select(
                            fact =>
                                new BatchFactVerificationResultDto
                                {
                                    FactId = fact.Id,
                                    Status =
                                        FactVerificationStatus
                                            .InsufficientEvidence,
                                    VerificationConfidence = 0,
                                    SupportingSourceCount = 0,
                                    ContradictingSourceCount = 0,
                                    Summary =
                                        "Fact verification servisi " +
                                        "geçici olarak kullanılamadı."
                                })
                        .ToList();
            }

            foreach (var fact in allFacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var verification =
                    verificationResults.FirstOrDefault(
                        x => x.FactId == fact.Id);

                if (verification is null)
                {
                    fact.VerificationStatus =
                        FactVerificationStatus.InsufficientEvidence;

                    fact.VerificationConfidence = 0;

                    fact.SupportingSourceCount = 0;

                    fact.ContradictingSourceCount = 0;

                    fact.VerificationSummary =
                        "Verification sonucu bulunamadı.";

                    fact.VerifiedAt =
                        DateTime.UtcNow;

                    continue;
                }

                fact.VerificationStatus =
                    verification.Status;

                fact.VerificationConfidence =
                    Math.Clamp(
                        verification.VerificationConfidence,
                        0.0,
                        1.0);

                fact.SupportingSourceCount =
                    Math.Max(
                        0,
                        verification.SupportingSourceCount);

                fact.ContradictingSourceCount =
                    Math.Max(
                        0,
                        verification.ContradictingSourceCount);

                fact.VerificationSummary =
                    verification.Summary;

                fact.VerifiedAt =
                    DateTime.UtcNow;

                Console.WriteLine();
                Console.WriteLine("--------------------------------------");
                Console.WriteLine("FACT VERIFICATION RESULT");
                Console.WriteLine($"Fact ID: {fact.Id}");
                Console.WriteLine($"Claim: {fact.Claim}");
                Console.WriteLine(
                    $"Status: {fact.VerificationStatus}");
                Console.WriteLine(
                    $"Confidence: {fact.VerificationConfidence}");
                Console.WriteLine(
                    $"Supporting Sources: " +
                    $"{fact.SupportingSourceCount}");
                Console.WriteLine(
                    $"Contradicting Sources: " +
                    $"{fact.ContradictingSourceCount}");
                Console.WriteLine(
                    $"Summary: {fact.VerificationSummary}");
                Console.WriteLine("--------------------------------------");
            }

            await _researchRepository.UpdateFactsAsync(
                allFacts,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine(
                "Verification tamamlandı.");

            Console.WriteLine(
                $"Toplam fact: {allFacts.Count}");

            Console.WriteLine(
                $"Supported: " +
                $"{allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.Supported)}");

            Console.WriteLine(
                $"PartiallySupported: " +
                $"{allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.PartiallySupported)}");

            Console.WriteLine(
                $"Contradicted: " +
                $"{allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.Contradicted)}");

            Console.WriteLine(
                $"InsufficientEvidence: " +
                $"{allFacts.Count(x =>
                    x.VerificationStatus ==
                    FactVerificationStatus.InsufficientEvidence)}");

            // =====================================================
            // 6. REPORT GENERATION
            // =====================================================

            research.Status =
                ResearchStatus.GeneratingReport;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("REPORT GENERATION STARTED");
            Console.WriteLine("======================================");

            var reportableFacts =
                allFacts
                    .Where(x =>
                        x.VerificationStatus ==
                            FactVerificationStatus.Supported ||
                        x.VerificationStatus ==
                            FactVerificationStatus.PartiallySupported)
                    .ToList();

            Console.WriteLine(
                $"Rapor için kullanılacak fact sayısı: " +
                $"{reportableFacts.Count}");

            if (reportableFacts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Rapor oluşturmak için doğrulanmış fact bulunamadı.");
            }

            var report =
                await _reportGenerator.GenerateAsync(
                    research,
                    reportableFacts,
                    cancellationToken);

            await _researchRepository.AddReportAsync(
                report,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine(
                "Report başarıyla oluşturuldu.");

            // =====================================================
            // 7. COMPLETED
            // =====================================================

            research.Status =
                ResearchStatus.Completed;

            research.CompletedAt =
                DateTime.UtcNow;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("RESEARCH COMPLETED");
            Console.WriteLine("======================================");

            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
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

            Console.WriteLine(
                $"Exception: {ex}");

            try
            {
                research.Status =
                    ResearchStatus.Failed;

                await _researchRepository.UpdateAsync(
                    research,
                    cancellationToken);
            }
            catch (Exception updateException)
            {
                Console.WriteLine(
                    "Research Failed statusu database'e " +
                    "kaydedilemedi.");

                Console.WriteLine(
                    $"Update error: {updateException}");
            }

            return true;
        }
    }
}