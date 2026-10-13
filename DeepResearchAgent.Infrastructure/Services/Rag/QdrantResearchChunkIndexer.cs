
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Rag;

public sealed class QdrantResearchChunkIndexer : IResearchChunkIndexer
{
    private const string CollectionName = "research_chunks";
    private const int ChunkSize = 1200;
    private const int ChunkOverlap = 150;
    private const int EmbeddingBatchSize = 4;

    private readonly HttpClient _httpClient;
    private readonly IEmbeddingService _embeddingService;

    public QdrantResearchChunkIndexer(
        HttpClient httpClient,
        IEmbeddingService embeddingService)
    {
        _httpClient = httpClient;
        _embeddingService = embeddingService;
    }

    public async Task IndexSourcesAsync(
        IReadOnlyCollection<Source> sources,
        CancellationToken cancellationToken = default)
    {
        var chunks = sources
            .Where(s =>
                s.CrawlSucceeded &&
                !string.IsNullOrWhiteSpace(s.Content))
            .SelectMany(CreateChunks)
            .ToList();

        if (chunks.Count == 0)
        {
            Console.WriteLine("Qdrant: indekslenecek metin parçası yok.");
            return;
        }

        Console.WriteLine($"Qdrant: indekslenecek chunk sayısı: {chunks.Count}");

        for (var i = 0; i < chunks.Count; i += EmbeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = chunks
                .Skip(i)
                .Take(EmbeddingBatchSize)
                .ToList();

            var embeddings =
                await _embeddingService.GenerateEmbeddingsAsync(
                    batch.Select(x => x.Text).ToArray(),
                    "passage",
                    cancellationToken);

            if (embeddings.Count != batch.Count)
            {
                throw new InvalidOperationException(
                    "Embedding sayısı chunk sayısıyla eşleşmiyor.");
            }

            var points = batch.Select((chunk, index) => new QdrantPoint
            {
                Id = Guid.NewGuid(),
                Vector = embeddings[index],
                Payload = new Dictionary<string, object>
                {
                    ["researchId"] = chunk.ResearchId.ToString(),
                    ["sourceId"] = chunk.SourceId.ToString(),
                    ["sourceUrl"] = chunk.SourceUrl,
                    ["sourceTitle"] = chunk.SourceTitle ?? "",
                    ["chunkIndex"] = chunk.ChunkIndex,
                    ["text"] = chunk.Text
                }
            }).ToList();

            var request = new QdrantUpsertRequest { Points = points };

            using var response = await _httpClient.PutAsJsonAsync(
                $"collections/{CollectionName}/points?wait=true",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                throw new HttpRequestException(
                    $"Qdrant upsert başarısız. " +
                    $"HTTP {(int)response.StatusCode}: {error}");
            }

            Console.WriteLine(
                $"Qdrant: {Math.Min(i + batch.Count, chunks.Count)}/{chunks.Count} chunk kaydedildi.");
        }
    }

    private static IEnumerable<SourceChunk> CreateChunks(Source source)
    {
        var content = source.Content;

        if (string.IsNullOrWhiteSpace(content))
            yield break;

        // Boşlukları normalize et, fakat metnin kelime sırasını koru.
        content = string.Join(
            " ",
            content.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        var start = 0;
        var chunkIndex = 0;

        while (start < content.Length)
        {
            var length = Math.Min(ChunkSize, content.Length - start);

            // Mümkünse kelimenin ortasında kesme.
            if (start + length < content.Length)
            {
                var lastSpace = content.LastIndexOf(
                    ' ',
                    start + length - 1,
                    length);

                if (lastSpace > start + (length / 2))
                    length = lastSpace - start;
            }

            var text = content.Substring(start, length).Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new SourceChunk(
                    source.ResearchId,
                    source.Id,
                    source.Url,
                    source.Title,
                    chunkIndex++,
                    text);
            }

            var nextStart = start + length - ChunkOverlap;

            // Kısa son parçada sonsuz döngüyü engelle.
            if (nextStart <= start)
                break;

            start = nextStart;
        }
    }

    private sealed record SourceChunk(
        Guid ResearchId,
        Guid SourceId,
        string SourceUrl,
        string? SourceTitle,
        int ChunkIndex,
        string Text);

    private sealed class QdrantUpsertRequest
    {
        [JsonPropertyName("points")]
        public List<QdrantPoint> Points { get; init; } = [];
    }

    private sealed class QdrantPoint
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; }

        [JsonPropertyName("vector")]
        public float[] Vector { get; init; } = [];

        [JsonPropertyName("payload")]
        public Dictionary<string, object> Payload { get; init; } = [];
    }
}
