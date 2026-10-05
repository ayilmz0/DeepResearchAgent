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
    public ResearchService(IResearchRepository researchRepository,
        IResearchPlanner researchPlanner,
        IResearchSearcher researchSearcher,
        ICrawler crawler,
        IResearchAnalyzer researchAnalyzer)
    {
        _researchRepository = researchRepository;
        _researchPlanner = researchPlanner;
        _researchSearcher = researchSearcher;
        _crawler = crawler;
        _researchAnalyzer = researchAnalyzer;
    }

    public async Task<bool> ProcessPendingResearchAsync(
    CancellationToken cancellationToken = default)
    {
        var research = await _researchRepository.GetPendingResearchAsync(
            cancellationToken);

        if (research is null)
        {
            return false;
        }

        research.Status = ResearchStatus.Planning;
        research.StartedAt = DateTime.UtcNow;

        var tasks = await _researchPlanner.CreatePlanAsync(
            research,
            cancellationToken);

        await _researchRepository.AddTasksAsync(
            tasks,
            cancellationToken);

        research.Status = ResearchStatus.Searching;

        await _researchRepository.UpdateAsync(
            research,
            cancellationToken);

        var pendingTasks =
            await _researchRepository.GetPendingTasksAsync(
                research.Id,
                cancellationToken);

        foreach (var task in pendingTasks)
        {
            var searchResults =
                await _researchSearcher.SearchAsync(
                    task.Query,
                    cancellationToken);

            foreach (var result in searchResults)
            {
                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    ResearchId = research.Id,
                    Url = result.Url,
                    Title = result.Title,
                    Content = result.Snippet,
                    CrawledAt = DateTime.UtcNow,
                    Depth = task.Depth,
                    RelevanceScore = 0
                };

                try
                {
                    var content = await _crawler.CrawlAsync(
                        source.Url,
                        cancellationToken);

                    source.Content = content;
                    source.CrawledAt = DateTime.UtcNow;
                    source.CrawlSucceeded = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Crawler failed: {source.Url} - {ex.Message}");

                    source.CrawlSucceeded = false;
                }

                await _researchRepository.AddSourcesAsync(
        new[] { source },
        cancellationToken);

                var facts = await _researchAnalyzer.AnalyzeAsync(
                    source,
                    cancellationToken);

                await _researchRepository.AddFactsAsync(
                    facts,
                    cancellationToken);
            }

            task.Status = ResearchStatus.Completed;
        }

        await _researchRepository.UpdateAsync(
            research,
            cancellationToken);

        return true;
    }

    public async Task<CreateResearchResponse> CreateResearchAsync(
        CreateResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var research = new Research
        {
            Id = Guid.NewGuid(),
            Query = request.Query,
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
}