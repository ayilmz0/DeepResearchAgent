
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Helpers;
using DeepResearchAgent.Engine.Interfaces;
using System.Text.RegularExpressions;

namespace DeepResearchAgent.Engine.Services;

public class ResearchService : IResearchService
{
    private readonly IResearchRepository _researchRepository;
    private readonly IResearchPlanner _researchPlanner;
    private readonly IResearchSearcher _researchSearcher;
    private readonly ICrawler _crawler;
    private readonly IResearchAnalyzer _researchAnalyzer;
    private readonly IReportGenerator _reportGenerator;
    private readonly ISourceRelevanceAnalyzer _sourceRelevanceAnalyzer;
    private readonly IResearchChunkIndexer _chunkIndexer;

    public ResearchService(
        IResearchRepository researchRepository,
        IResearchPlanner researchPlanner,
        IResearchSearcher researchSearcher,
        ICrawler crawler,
        IResearchAnalyzer researchAnalyzer,
        IReportGenerator reportGenerator,
        ISourceRelevanceAnalyzer sourceRelevanceAnalyzer,
        IResearchChunkIndexer chunkIndexer)
    {
        _researchRepository = researchRepository;
        _researchPlanner = researchPlanner;
        _researchSearcher = researchSearcher;
        _crawler = crawler;
        _researchAnalyzer = researchAnalyzer;
        _reportGenerator = reportGenerator;
        _sourceRelevanceAnalyzer = sourceRelevanceAnalyzer;
        _chunkIndexer = chunkIndexer;
    }

    private static string NormalizeDoi(string doi)
    {
        doi = doi.Trim();

        string[] prefixes =
        [
            "https://doi.org/",
            "http://doi.org/",
            "https://dx.doi.org/",
            "http://dx.doi.org/",
            "doi:"
        ];

        foreach (var prefix in prefixes)
        {
            if (doi.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                doi = doi[prefix.Length..].Trim();
                break;
            }
        }

        return doi.TrimEnd('/').ToLowerInvariant();
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
        var research = await _researchRepository.GetByIdAsync(
            id,
            cancellationToken);

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
                researchId,
                cancellationToken);

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
                research,
                cancellationToken);

            // 1. PLANNING
            research.Status = ResearchStatus.Planning;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine("PLANNING STARTED");

            var tasks = await _researchPlanner.CreatePlanAsync(
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

            // 2. SEARCHING
            research.Status = ResearchStatus.Searching;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine("SEARCHING STARTED");

            var allSearchResults = new List<SearchResultDto>();

            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"Search task: {task.Query}");

                try
                {
                    var searchResults =
                        await _researchSearcher.SearchAsync(
                            task.Query,
                            cancellationToken);

                    if (searchResults is not null)
                        allSearchResults.AddRange(searchResults);

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
                    Console.WriteLine(ex);

                    task.Status = ResearchStatus.Failed;

                    await _researchRepository.UpdateTaskAsync(
                        task,
                        cancellationToken);
                }
            }

            var distinctSearchResults = allSearchResults
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(
                    x => UrlNormalizer.Normalize(x.Url),
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
                research,
                cancellationToken);

            Console.WriteLine("CRAWLING STARTED");

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
                    NormalizedUrl = UrlNormalizer.Normalize(
                        searchResult.Url),
                    Title = searchResult.Title,
                    Depth = 0,
                    RelevanceScore = 0,
                    CrawlSucceeded = false,
                    CrawledAt = DateTime.UtcNow
                };

                try
                {
                    var crawlResult = await _crawler.CrawlAsync(
                        searchResult.Url,
                        cancellationToken);

                    source.Content = crawlResult.Content;
                    source.Doi = crawlResult.Doi;

                    if (!string.IsNullOrWhiteSpace(crawlResult.Title))
                        source.Title = crawlResult.Title;

                    if (!string.IsNullOrWhiteSpace(
                            crawlResult.CanonicalUrl))
                    {
                        source.NormalizedUrl =
                            UrlNormalizer.Normalize(
                                crawlResult.CanonicalUrl);
                    }

                    source.CrawlSucceeded =
                        !string.IsNullOrWhiteSpace(source.Content);

                    Console.WriteLine(
                        $"Crawl başarılı: {source.CrawlSucceeded}");

                    Console.WriteLine(
                        $"İçerik uzunluğu: {source.Content?.Length ?? 0}");
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
                    Console.WriteLine(ex);

                    source.CrawlSucceeded = false;
                }

                sources.Add(source);
            }

            // Aynı DOI veya URL ile gelen kaynakları tekilleştir.
            sources = sources
                .GroupBy(
                    source => !string.IsNullOrWhiteSpace(source.Doi)
                        ? $"doi:{NormalizeDoi(source.Doi)}"
                        : $"url:{UrlNormalizer.Normalize(
                            source.NormalizedUrl ?? source.Url)}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(source => source.CrawlSucceeded)
                    .ThenByDescending(
                        source => source.Content?.Length ?? 0)
                    .First())
                .ToList();

            Console.WriteLine(
                $"Tekilleştirme sonrası kaynak sayısı: {sources.Count}");

            var crawlableSources = sources
                .Where(source =>
                    source.CrawlSucceeded &&
                    !string.IsNullOrWhiteSpace(source.Content))
                .ToList();

            if (crawlableSources.Count == 0)
            {
                throw new InvalidOperationException(
                    "Hiçbir kaynak başarıyla taranamadı.");
            }

            // 4. SOURCE RELEVANCE FILTER
            Console.WriteLine("SOURCE RELEVANCE FILTER STARTED");

            var relevanceResults =
                await _sourceRelevanceAnalyzer.AnalyzeBatchAsync(
                    research,
                    crawlableSources,
                    cancellationToken);

            var relevantSourceIds = relevanceResults
                .Where(result => result.IsRelevant)
                .Select(result => result.SourceId)
                .ToHashSet();

            sources = crawlableSources
                .Where(source => relevantSourceIds.Contains(source.Id))
                .ToList();

            Console.WriteLine(
                $"Taranabilir kaynak: {crawlableSources.Count}");
            Console.WriteLine(
                $"İlgili kaynak: {sources.Count}");

            if (sources.Count == 0)
            {
                throw new InvalidOperationException(
                    "Araştırma konusuyla ilgili kaynak bulunamadı.");
            }

            // 5. PERSIST SOURCES AND INDEX FOR RAG
            await _researchRepository.AddSourcesAsync(
                sources,
                cancellationToken);

            Console.WriteLine("RAG: kaynaklar Qdrant'a indeksleniyor...");

            await _chunkIndexer.IndexSourcesAsync(
                sources,
                cancellationToken);

            Console.WriteLine("RAG: kaynak indeksleme tamamlandı.");
            Console.WriteLine($"Kaynak sayısı: {sources.Count}");

            // 6. ANALYZING
            research.Status = ResearchStatus.Analyzing;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine("ANALYZING STARTED");

            var successfulSources = sources
                .Where(source =>
                    source.CrawlSucceeded &&
                    !string.IsNullOrWhiteSpace(source.Content))
                .ToList();

            var allFacts = new List<Fact>();

            foreach (var source in successfulSources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"Analyzing source: {source.Title}");
                Console.WriteLine($"URL: {source.Url}");

                try
                {
                    var facts = await _researchAnalyzer.AnalyzeAsync(
                        research,
                        source,
                        cancellationToken);

                    if (facts is null || facts.Count == 0)
                    {
                        Console.WriteLine(
                            "Bu kaynak için fact bulunamadı.");
                        continue;
                    }

                    foreach (var fact in facts)
                    {
                        fact.Id = fact.Id == Guid.Empty
                            ? Guid.NewGuid()
                            : fact.Id;

                        // Her fact'in çıkarıldığı kaynak korunur.
                        fact.SourceId = source.Id;
                    }

                    allFacts.AddRange(facts);

                    Console.WriteLine(
                        $"Kaynak bulgu sayısı: {facts.Count}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Kaynak analizi başarısız: {source.Url}");
                    Console.WriteLine(ex);
                }
            }

            allFacts = allFacts
                .Where(fact =>
                    !string.IsNullOrWhiteSpace(fact.Claim))
                .GroupBy(
                    fact => $"{fact.SourceId}:{fact.Claim.Trim()}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (allFacts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Analiz aşamasında herhangi bir bulgu çıkarılamadı.");
            }

            await _researchRepository.AddFactsAsync(
                allFacts,
                cancellationToken);

            Console.WriteLine($"Toplam bulgu sayısı: {allFacts.Count}");

            // 7. REPORT GENERATION
            // Artık fact doğrulama durumuna göre filtreleme yapılmaz.
            research.Status = ResearchStatus.GeneratingReport;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine("REPORT GENERATION STARTED");

            Console.WriteLine(
                $"Rapor için kullanılacak bulgu sayısı: {allFacts.Count}");

            var report = await _reportGenerator.GenerateAsync(
                research,
                allFacts,
                cancellationToken);

            // Rapor üreticisinin, bağımsız çapraz doğrulama yapılmadığını
            // rapor metninde açıkça belirtmesi gerekir.
            await _researchRepository.AddReportAsync(
                report,
                cancellationToken);

            Console.WriteLine("Report başarıyla oluşturuldu.");

            // 8. COMPLETED
            research.Status = ResearchStatus.Completed;
            research.CompletedAt = DateTime.UtcNow;

            await _researchRepository.UpdateAsync(
                research,
                cancellationToken);

            Console.WriteLine("RESEARCH COMPLETED");

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
            Console.WriteLine("RESEARCH FAILED");
            Console.WriteLine($"Exception: {ex}");

            try
            {
                research.Status = ResearchStatus.Failed;
                research.CompletedAt = DateTime.UtcNow;

                await _researchRepository.UpdateAsync(
                    research,
                    CancellationToken.None);
            }
            catch (Exception updateException)
            {
                Console.WriteLine(
                    "Research Failed durumu database'e kaydedilemedi.");
                Console.WriteLine(updateException);
            }

            return true;
        }
    }
}
