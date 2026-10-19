
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using DeepResearchAgent.Infrastructure.Services.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepResearchAgent.Tests;

public sealed class ReportGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ValidResponse_ReturnsReportWithMethodology()
    {
        // Arrange
        var research = CreateResearch();
        var facts = new List<Fact>
        {
            new()
            {
                Id = Guid.NewGuid(),
                SourceId = Guid.NewGuid(),
                Claim = "Yapay zeka üretim süreçlerinde kullanılır.",
                Value = "Otomasyon"
            }
        };

        var retriever = new FakeChunkRetriever();
        var aiClient = new FakeAIClient(
            """{"title":"Test Raporu","content":"## Bulgular\nTest içeriği"}""");

        var generator = CreateGenerator(aiClient, retriever);

        // Act
        var report = await generator.GenerateAsync(research, facts);

        // Assert
        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal(research.Id, report.ResearchId);
        Assert.Equal("Test Raporu", report.Title);
        Assert.Contains("Test içeriği", report.Content);
        Assert.Contains("## Metodoloji ve Sınırlılıklar", report.Content);
        Assert.Contains("1 yapılandırılmış bulgu", report.Content);
        Assert.Equal(1, aiClient.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_NoFactsAndNoChunks_ThrowsException()
    {
        // Arrange
        var retriever = new FakeChunkRetriever();
        var aiClient = new FakeAIClient(
            """{"title":"Test","content":"İçerik"}""");

        var generator = CreateGenerator(aiClient, retriever);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(
                CreateResearch(),
                Array.Empty<Fact>()));

        // Assert
        Assert.Contains("bulgusu", exception.Message);
        Assert.Equal(0, aiClient.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_EmptyAIResponse_ThrowsException()
    {
        // Arrange
        var aiClient = new FakeAIClient("");
        var generator = CreateGenerator(
            aiClient,
            new FakeChunkRetriever());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(
                CreateResearch(),
                new List<Fact>
                {
                    new() { Claim = "Test bulgusu" }
                }));

        Assert.Equal(1, aiClient.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_InvalidJson_ThrowsException()
    {
        // Arrange
        var aiClient = new FakeAIClient("Bu bir JSON yanıtı değil.");
        var generator = CreateGenerator(
            aiClient,
            new FakeChunkRetriever());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(
                CreateResearch(),
                new List<Fact>
                {
                    new() { Claim = "Test bulgusu" }
                }));
    }

    [Fact]
    public async Task GenerateAsync_EmptyTitle_ThrowsException()
    {
        // Arrange
        var aiClient = new FakeAIClient(
            """{"title":" ","content":"Geçerli içerik"}""");

        var generator = CreateGenerator(
            aiClient,
            new FakeChunkRetriever());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(
                CreateResearch(),
                new List<Fact>
                {
                    new() { Claim = "Test bulgusu" }
                }));
    }

    [Fact]
    public async Task GenerateAsync_ChunkExists_IncludesSourceDetailsInPrompt()
    {
        // Arrange
        var research = CreateResearch();

        var retriever = new FakeChunkRetriever
        {
            Results =
            [
                new ResearchChunkSearchResultDto(
                    Guid.NewGuid(),
                    "Kaynak başlığı",
                    "https://example.com/research",
                    0,
                    "Kaynak metnindeki önemli bilgi.",
                    0.95)
            ]
        };

        var aiClient = new FakeAIClient(
            """{"title":"Kaynaklı Rapor","content":"Rapor içeriği"}""");

        var generator = CreateGenerator(aiClient, retriever);

        // Act
        var report = await generator.GenerateAsync(
            research,
            Array.Empty<Fact>());

        // Assert
        Assert.Equal("Kaynaklı Rapor", report.Title);
        Assert.Contains("Kaynak başlığı", aiClient.LastPrompt);
        Assert.Contains("https://example.com/research", aiClient.LastPrompt);
        Assert.Contains("Kaynak metnindeki önemli bilgi.", aiClient.LastPrompt);
        Assert.Equal(1, retriever.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_PreCancelledToken_ThrowsCancellationException()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var retriever = new FakeChunkRetriever();
        var aiClient = new FakeAIClient(
            """{"title":"Test","content":"İçerik"}""");

        var generator = CreateGenerator(aiClient, retriever);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => generator.GenerateAsync(
                CreateResearch(),
                Array.Empty<Fact>(),
                cancellationTokenSource.Token));

        Assert.Equal(0, retriever.CallCount);
        Assert.Equal(0, aiClient.CallCount);
    }

    private static Research CreateResearch()
    {
        return new Research
        {
            Id = Guid.NewGuid(),
            Query = "Yapay zekanın tekstil sektörüne etkisi"
        };
    }

    private static ReportGenerator CreateGenerator(
        FakeAIClient aiClient,
        FakeChunkRetriever retriever)
    {
        return new ReportGenerator(
            aiClient,
            retriever,
            NullLogger<ReportGenerator>.Instance);
    }

    private sealed class FakeAIClient(string response) : IAIClient
    {
        public int CallCount { get; private set; }

        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> GenerateAsync(
            string prompt,
            object? responseSchema = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;

            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(response);
        }
    }

    private sealed class FakeChunkRetriever : IResearchChunkRetriever
    {
        public IReadOnlyList<ResearchChunkSearchResultDto> Results { get; set; }
            = Array.Empty<ResearchChunkSearchResultDto>();

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ResearchChunkSearchResultDto>> SearchAsync(
            Guid researchId,
            string query,
            int topK = 5,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Results);
        }
    }
}
