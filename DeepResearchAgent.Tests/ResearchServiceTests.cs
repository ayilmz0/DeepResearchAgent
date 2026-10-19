using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepResearchAgent.Tests;

public sealed class ResearchServiceTests
{
    [Fact]
    public async Task ProcessPendingResearchAsync_NoPendingResearch_ReturnsFalse()
    {
        // Arrange
        var repository = new FakeResearchRepository();
        var service = CreateService(repository);

        // Act
        var result = await service.ProcessPendingResearchAsync();

        // Assert
        Assert.False(result);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task ProcessPendingResearchAsync_PlanningFails_MarksResearchAsFailed()
    {
        // Arrange
        var research = CreateResearch();

        var repository = new FakeResearchRepository
        {
            PendingResearch = research
        };

        var planner = new FakeResearchPlanner
        {
            ExceptionToThrow = new InvalidOperationException(
                "Test planlama hatası")
        };

        var service = CreateService(repository, planner);

        // Act
        var result = await service.ProcessPendingResearchAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(ResearchStatus.Failed, research.Status);
        Assert.NotNull(research.CompletedAt);
        Assert.True(repository.UpdateCallCount > 0);
        Assert.Equal(research.Id, repository.LastUpdatedResearch?.Id);
        Assert.Empty(repository.AddedReports);
    }

    [Fact]
    public async Task ProcessPendingResearchAsync_ValidResearch_SavesReportAndCompletes()
    {
        // Arrange
        var research = CreateResearch();

        var repository = new FakeResearchRepository
        {
            PendingResearch = research
        };

        var reportGenerator = new FakeReportGenerator();

        var service = CreateService(
            repository,
            reportGenerator: reportGenerator);

        // Act
        var result = await service.ProcessPendingResearchAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(ResearchStatus.Completed, research.Status);
        Assert.NotNull(research.StartedAt);
        Assert.NotNull(research.CompletedAt);

        Assert.Single(repository.AddedTasks);
        Assert.Single(repository.AddedSources);
        Assert.Single(repository.AddedFacts);
        Assert.Single(repository.AddedReports);

        var savedSource = repository.AddedSources[0];
        var savedFact = repository.AddedFacts[0];
        var savedReport = repository.AddedReports[0];

        Assert.True(savedSource.CrawlSucceeded);
        Assert.Equal(research.Id, savedSource.ResearchId);

        Assert.Equal(savedSource.Id, savedFact.SourceId);
        Assert.False(string.IsNullOrWhiteSpace(savedFact.Claim));

        Assert.Equal(research.Id, savedReport.ResearchId);
        Assert.Equal("Test raporu", savedReport.Title);
        Assert.Equal("Test içeriği", savedReport.Content);
    }

    [Fact]
    public async Task ProcessPendingResearchAsync_NoSearchResults_MarksResearchAsFailed()
    {
        // Arrange
        var research = CreateResearch();

        var repository = new FakeResearchRepository
        {
            PendingResearch = research
        };

        var searcher = new FakeResearchSearcher
        {
            Results = Array.Empty<SearchResultDto>()
        };

        var reportGenerator = new FakeReportGenerator();

        var service = CreateService(
            repository,
            searcher: searcher,
            reportGenerator: reportGenerator);

        // Act
        var result = await service.ProcessPendingResearchAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(ResearchStatus.Failed, research.Status);
        Assert.NotNull(research.CompletedAt);
        Assert.Empty(repository.AddedSources);
        Assert.Empty(repository.AddedFacts);
        Assert.Empty(repository.AddedReports);
    }

    [Fact]
    public async Task ProcessPendingResearchAsync_NoFacts_MarksResearchAsFailed()
    {
        // Arrange
        var research = CreateResearch();

        var repository = new FakeResearchRepository
        {
            PendingResearch = research
        };

        var analyzer = new FakeResearchAnalyzer
        {
            Facts = Array.Empty<Fact>()
        };

        var service = CreateService(
            repository,
            analyzer: analyzer);

        // Act
        var result = await service.ProcessPendingResearchAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(ResearchStatus.Failed, research.Status);
        Assert.NotNull(research.CompletedAt);

        Assert.Single(repository.AddedSources);
        Assert.Empty(repository.AddedFacts);
        Assert.Empty(repository.AddedReports);
    }

    private static Research CreateResearch()
    {
        return new Research
        {
            Id = Guid.NewGuid(),
            Query = "Yapay zekanın tekstil sektörüne etkisi",
            Status = ResearchStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static DeepResearchAgent.Engine.Services.ResearchService CreateService(
        FakeResearchRepository repository,
        FakeResearchPlanner? planner = null,
        FakeResearchSearcher? searcher = null,
        FakeResearchAnalyzer? analyzer = null,
        FakeReportGenerator? reportGenerator = null)
    {
        return new DeepResearchAgent.Engine.Services.ResearchService(
            repository,
            planner ?? new FakeResearchPlanner(),
            searcher ?? new FakeResearchSearcher(),
            new FakeCrawler(),
            analyzer ?? new FakeResearchAnalyzer(),
            reportGenerator ?? new FakeReportGenerator(),
            new FakeSourceRelevanceAnalyzer(),
            new FakeResearchChunkIndexer(),
            NullLogger<DeepResearchAgent.Engine.Services.ResearchService>.Instance);
    }

    private sealed class FakeResearchRepository : IResearchRepository
    {
        public Research? PendingResearch { get; set; }

        public int UpdateCallCount { get; private set; }

        public Research? LastUpdatedResearch { get; private set; }

        public List<ResearchTask> AddedTasks { get; } = [];

        public List<Source> AddedSources { get; } = [];

        public List<Fact> AddedFacts { get; } = [];

        public List<Report> AddedReports { get; } = [];

        public Task AddAsync(
            Research research,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<Research?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Research?>(null);
        }

        public Task<Research?> GetPendingResearchAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PendingResearch);
        }

        public Task AddTasksAsync(
            IEnumerable<ResearchTask> tasks,
            CancellationToken cancellationToken = default)
        {
            AddedTasks.AddRange(tasks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResearchTask>> GetPendingTasksAsync(
            Guid researchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ResearchTask>>(
                Array.Empty<ResearchTask>());
        }

        public Task AddSourcesAsync(
            IEnumerable<Source> sources,
            CancellationToken cancellationToken = default)
        {
            AddedSources.AddRange(sources);
            return Task.CompletedTask;
        }

        public Task AddFactsAsync(
            IEnumerable<Fact> facts,
            CancellationToken cancellationToken = default)
        {
            AddedFacts.AddRange(facts);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Fact>> GetFactsByResearchIdAsync(
            Guid researchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Fact>>(
                Array.Empty<Fact>());
        }

        public Task UpdateFactsAsync(
            IEnumerable<Fact> facts,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateTaskAsync(
            ResearchTask task,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            Research research,
            CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            LastUpdatedResearch = research;
            return Task.CompletedTask;
        }

        public Task<Report?> GetReportByResearchIdAsync(
            Guid researchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Report?>(null);
        }

        public Task AddCitationsAsync(
            IEnumerable<Citation> citations,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task AddReportAsync(
            Report report,
            CancellationToken cancellationToken = default)
        {
            AddedReports.Add(report);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeResearchPlanner : IResearchPlanner
    {
        public Exception? ExceptionToThrow { get; set; }

        public Task<IReadOnlyList<ResearchTask>> CreatePlanAsync(
            Research research,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            IReadOnlyList<ResearchTask> tasks =
            [
                new ResearchTask
                {
                    Id = Guid.NewGuid(),
                    ResearchId = research.Id,
                    Query = research.Query,
                    Status = ResearchStatus.Pending,
                    Depth = 0,
                    Research = research
                }
            ];

            return Task.FromResult(tasks);
        }
    }

    private sealed class FakeResearchSearcher : IResearchSearcher
    {
        public IReadOnlyList<SearchResultDto> Results { get; set; } =
        [
            new SearchResultDto
            {
                Title = "Test kaynağı",
                Url = "https://example.com/research",
                Snippet = "Test araştırma özeti"
            }
        ];

        public Task<IReadOnlyList<SearchResultDto>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Results);
        }
    }

    private sealed class FakeCrawler : ICrawler
    {
        public Task<CrawlResultDto> CrawlAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CrawlResultDto
            {
                Title = "Test kaynağı",
                CanonicalUrl = url,
                Content = "Yapay zeka üretim süreçlerinde otomasyon sağlar."
            });
        }
    }

    private sealed class FakeResearchAnalyzer : IResearchAnalyzer
    {
        public IReadOnlyList<Fact> Facts { get; set; } =
        [
            new Fact
            {
                Claim = "Yapay zeka üretim süreçlerinde otomasyon sağlar.",
                Value = "Otomasyon"
            }
        ];

        public Task<IReadOnlyList<Fact>> AnalyzeAsync(
            Research research,
            Source source,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Facts);
        }
    }

    private sealed class FakeReportGenerator : IReportGenerator
    {
        public Task<Report> GenerateAsync(
            Research research,
            IReadOnlyList<Fact> facts,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Report
            {
                Id = Guid.NewGuid(),
                ResearchId = research.Id,
                Title = "Test raporu",
                Content = "Test içeriği",
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private sealed class FakeSourceRelevanceAnalyzer
        : ISourceRelevanceAnalyzer
    {
        public Task<IReadOnlyList<SourceRelevanceResultDto>> AnalyzeBatchAsync(
            Research research,
            IReadOnlyList<Source> sources,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SourceRelevanceResultDto> results = sources
                .Select(source => new SourceRelevanceResultDto
                {
                    SourceId = source.Id,
                    IsRelevant = true,
                    RelevanceScore = 0.95,
                    Confidence = 0.95,
                    Reason = "Test için ilgili kaynak"
                })
                .ToList();

            return Task.FromResult(results);
        }
    }

    private sealed class FakeResearchChunkIndexer : IResearchChunkIndexer
    {
        public Task IndexSourcesAsync(
            IReadOnlyCollection<Source> sources,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
