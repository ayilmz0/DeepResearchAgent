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

    public ResearchService(
        IResearchRepository researchRepository,
        IResearchPlanner researchPlanner,
        IResearchSearcher researchSearcher,
        ICrawler crawler,
        IResearchAnalyzer researchAnalyzer,
        IFactVerifier factVerifier)
    {
        _researchRepository = researchRepository;
        _researchPlanner = researchPlanner;
        _researchSearcher = researchSearcher;
        _crawler = crawler;
        _researchAnalyzer = researchAnalyzer;
        _factVerifier = factVerifier;
    }

    public async Task<CreateResearchResponse> CreateResearchAsync(
        CreateResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException(
                "Araştırma konusu boş olamaz.",
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

        try
        {
            // =====================================================
            // 1. PLANNING
            // =====================================================

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("RESEARCH STARTED");
            Console.WriteLine($"Research ID: {research.Id}");
            Console.WriteLine($"Query: {research.Query}");
            Console.WriteLine("======================================");

            research.Status = ResearchStatus.Planning;
            research.StartedAt = DateTime.UtcNow;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine(
                "Research Planner çalışıyor...");

            var tasks =
                await _researchPlanner.CreatePlanAsync(
                    research,
                    cancellationToken);

            Console.WriteLine(
                $"Planner {tasks.Count} task oluşturdu.");

            await _researchRepository.AddTasksAsync(
                tasks,
                cancellationToken);

            // =====================================================
            // 2. SEARCHING
            // =====================================================

            research.Status = ResearchStatus.Searching;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("SEARCHING");
            Console.WriteLine("======================================");

            var pendingTasks =
                await _researchRepository.GetPendingTasksAsync(
                    research.Id,
                    cancellationToken);

            Console.WriteLine(
                $"İşlenecek task sayısı: {pendingTasks.Count}");

            foreach (var task in pendingTasks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine();
                Console.WriteLine("--------------------------------------");
                Console.WriteLine("TASK");
                Console.WriteLine($"Task ID: {task.Id}");
                Console.WriteLine($"Query: {task.Query}");
                Console.WriteLine($"Depth: {task.Depth}");
                Console.WriteLine("--------------------------------------");

                var searchResults =
                    await _researchSearcher.SearchAsync(
                        task.Query,
                        cancellationToken);

                Console.WriteLine(
                    $"Tavily {searchResults.Count} sonuç döndürdü.");

                // =================================================
                // 3. CRAWLING
                // =================================================

                research.Status = ResearchStatus.Crawling;

                await _researchRepository.UpdateAsync(
                    research,
                    cancellationToken);

                var sources = new List<Source>();

                foreach (var result in searchResults)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(result.Url))
                    {
                        continue;
                    }

                    Console.WriteLine();
                    Console.WriteLine("======================================");
                    Console.WriteLine("CRAWLING SOURCE");
                    Console.WriteLine($"URL: {result.Url}");
                    Console.WriteLine("======================================");

                    var source = new Source
                    {
                        Id = Guid.NewGuid(),
                        ResearchId = research.Id,
                        Url = result.Url,
                        Title = result.Title,
                        Content = null,
                        Depth = task.Depth,
                        RelevanceScore = 0,
                        CrawlSucceeded = false,
                        CrawledAt = null
                    };

                    try
                    {
                        var crawledContent =
                            await _crawler.CrawlAsync(
                                result.Url,
                                cancellationToken);

                        if (!string.IsNullOrWhiteSpace(crawledContent))
                        {
                            source.Content = crawledContent;
                            source.CrawlSucceeded = true;

                            Console.WriteLine(
                                "Crawler başarılı.");

                            Console.WriteLine(
                                $"İçerik uzunluğu: {crawledContent.Length}");
                        }
                        else
                        {
                            Console.WriteLine(
                                "Crawler boş içerik döndürdü.");
                        }

                        source.CrawledAt = DateTime.UtcNow;
                    }
                    catch (HttpRequestException ex)
                    {
                        source.CrawlSucceeded = false;
                        source.CrawledAt = DateTime.UtcNow;

                        Console.WriteLine(
                            $"HTTP crawler hatası: {ex.Message}");

                        Console.WriteLine(
                            "Bu source atlanıyor.");
                    }
                    catch (InvalidOperationException ex)
                    {
                        source.CrawlSucceeded = false;
                        source.CrawledAt = DateTime.UtcNow;

                        Console.WriteLine(
                            $"Crawler içerik hatası: {ex.Message}");

                        Console.WriteLine(
                            "Bu source atlanıyor.");
                    }
                    catch (Exception ex)
                    {
                        source.CrawlSucceeded = false;
                        source.CrawledAt = DateTime.UtcNow;

                        Console.WriteLine(
                            $"Crawler beklenmeyen hata: {ex}");

                        Console.WriteLine(
                            "Bu source atlanıyor.");
                    }

                    sources.Add(source);
                }

                if (sources.Count > 0)
                {
                    await _researchRepository.AddSourcesAsync(
                        sources,
                        cancellationToken);

                    Console.WriteLine(
                        $"{sources.Count} source database'e kaydedildi.");
                }

                // =================================================
                // 4. ANALYZING
                // =================================================

                research.Status = ResearchStatus.Analyzing;

                await _researchRepository.UpdateAsync(
                    research,
                    cancellationToken);

                Console.WriteLine();
                Console.WriteLine("======================================");
                Console.WriteLine("ANALYZING");
                Console.WriteLine("======================================");

                // Sadece başarılı crawl edilen ve içeriği olan
                // source'lar analiz edilecek.
                //
                // Şimdilik Gemini quota'sını korumak için
                // research başına maksimum 5 source analiz ediyoruz.

                var analyzableSources = sources
                    .Where(x => x.CrawlSucceeded)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                    .Take(5)
                    .ToList();

                Console.WriteLine(
                    $"Analiz edilecek source sayısı: " +
                    $"{analyzableSources.Count}");

                foreach (var source in analyzableSources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Console.WriteLine();
                    Console.WriteLine("--------------------------------------");
                    Console.WriteLine("SOURCE ANALYSIS");
                    Console.WriteLine($"URL: {source.Url}");
                    Console.WriteLine($"Title: {source.Title}");
                    Console.WriteLine("--------------------------------------");

                    try
                    {
                        var facts =
                            await _researchAnalyzer.AnalyzeAsync(
                                research,
                                source,
                                cancellationToken);

                        Console.WriteLine(
                            $"Gemini {facts.Count} fact çıkardı.");

                        if (facts.Count == 0)
                        {
                            Console.WriteLine(
                                "Bu source için ilgili fact bulunamadı.");

                            continue;
                        }

                        await _researchRepository.AddFactsAsync(
                            facts,
                            cancellationToken);

                        Console.WriteLine(
                            $"{facts.Count} fact database'e kaydedildi.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            "!!! FACT EXTRACTION FAILED !!!");

                        Console.WriteLine(
                            $"Source: {source.Url}");

                        Console.WriteLine(
                            $"Exception: {ex}");

                        Console.WriteLine(
                            "Bu source analiz sırasında atlanıyor.");
                    }
                }

                // Task tamamlandı.
                task.Status = ResearchStatus.Completed;

                await _researchRepository.UpdateTaskAsync(
                    task,
                    cancellationToken);

                Console.WriteLine();
                Console.WriteLine(
                    $"Task tamamlandı: {task.Id}");
            }

            // =====================================================
            // 5. VERIFICATION
            // =====================================================

            research.Status = ResearchStatus.Verifying;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("VERIFICATION STARTED");
            Console.WriteLine("======================================");

            var allFacts =
                await _researchRepository.GetFactsByResearchIdAsync(
                    research.Id,
                    cancellationToken);

            Console.WriteLine(
                $"Verification yapılacak Fact sayısı: " +
                $"{allFacts.Count}");

            foreach (var fact in allFacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine();
                Console.WriteLine("--------------------------------------");
                Console.WriteLine("FACT VERIFICATION");
                Console.WriteLine($"Fact ID: {fact.Id}");
                Console.WriteLine($"Claim: {fact.Claim}");
                Console.WriteLine("--------------------------------------");

                try
                {
                    var verification =
                        await _factVerifier.VerifyAsync(
                            fact,
                            allFacts,
                            cancellationToken);

                    Console.WriteLine(
                        "Gemini verification sonucu alındı.");

                    Console.WriteLine(
                        $"Verification Confidence: " +
                        $"{verification.VerificationConfidence}");

                    Console.WriteLine(
                        $"Supporting Sources: " +
                        $"{verification.SupportingSourceCount}");

                    Console.WriteLine(
                        $"Contradicting Sources: " +
                        $"{verification.ContradictingSourceCount}");

                    fact.VerificationStatus =
                        verification.Status;

                    fact.VerificationConfidence =
                        verification.VerificationConfidence;

                    fact.SupportingSourceCount =
                        verification.SupportingSourceCount;

                    fact.ContradictingSourceCount =
                        verification.ContradictingSourceCount;

                    fact.VerificationSummary =
                        verification.Summary;

                    fact.VerifiedAt =
                        DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "!!! FACT VERIFICATION FAILED !!!");

                    Console.WriteLine(
                        $"Fact ID: {fact.Id}");

                    Console.WriteLine(
                        $"Claim: {fact.Claim}");

                    Console.WriteLine(
                        $"Exception: {ex}");

                    Console.WriteLine(
                        "Bu fact verification sırasında atlanıyor.");
                }
            }

            // =====================================================
            // 6. SAVE VERIFICATION RESULTS
            // =====================================================

            if (allFacts.Count > 0)
            {
                await _researchRepository.UpdateFactsAsync(
                    allFacts,
                    cancellationToken);

                Console.WriteLine();
                Console.WriteLine(
                    "Verification sonuçları database'e kaydedildi.");
            }

            // =====================================================
            // 7. COMPLETED
            // =====================================================

            research.Status = ResearchStatus.Completed;
            research.CompletedAt = DateTime.UtcNow;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("RESEARCH COMPLETED");
            Console.WriteLine($"Research ID: {research.Id}");
            Console.WriteLine("======================================");

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            research.Status = ResearchStatus.Failed;

            try
            {
                await _researchRepository.UpdateAsync(
                    research,
                    cancellationToken);
            }
            catch
            {
                // Ana hata zaten mevcut.
                // Status update başarısız olursa
                // ikinci bir exception ile asıl hatayı gizlemiyoruz.
            }

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("RESEARCH FAILED");
            Console.WriteLine("======================================");

            Console.WriteLine(
                $"Research ID: {research.Id}");

            Console.WriteLine(
                $"Exception: {ex}");

            Console.WriteLine(
                "======================================");

            return true;
        }
    }
}