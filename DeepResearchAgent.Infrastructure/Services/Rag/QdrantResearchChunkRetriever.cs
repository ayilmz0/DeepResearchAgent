
using System.Net.Http.Json;
using System.Text.Json;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Infrastructure.Services.Rag;

public sealed class QdrantResearchChunkRetriever : IResearchChunkRetriever
{
    private const string CollectionName = "research_chunks";

    private readonly HttpClient _httpClient;
    private readonly IEmbeddingService _embeddingService;

    public QdrantResearchChunkRetriever(
        HttpClient httpClient,
        IEmbeddingService embeddingService)
    {
        _httpClient = httpClient;
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<ResearchChunkSearchResultDto>> SearchAsync(
        Guid researchId,
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (researchId == Guid.Empty)
            throw new ArgumentException(
                "ResearchId boş olamaz.", nameof(researchId));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException(
                "Arama sorgusu boş olamaz.", nameof(query));

        if (topK is < 1 or > 50)
            throw new ArgumentOutOfRangeException(
                nameof(topK), "topK 1 ile 50 arasında olmalıdır.");

        // Arama metnini query tipiyle vektöre dönüştür.
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
            new[] { query },
            "query",
            cancellationToken);

        if (embeddings.Count != 1 || embeddings[0].Length == 0)
            throw new InvalidOperationException(
                "Arama sorgusu için geçerli embedding üretilemedi.");

        var requestBody = new
        {
            vector = embeddings[0],
            limit = topK,
            with_payload = true,
            filter = new
            {
                must = new[]
                {
                    new
                    {
                        key = "researchId",
                        match = new
                        {
                            value = researchId.ToString()
                        }
                    }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"collections/{CollectionName}/points/search",
            requestBody,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Qdrant araması başarısız. " +
                $"HTTP {(int)response.StatusCode}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Qdrant yanıtında result dizisi bulunamadı.");
        }

        var chunks = new List<ResearchChunkSearchResultDto>();

        foreach (var item in results.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.TryGetProperty("payload", out var payload))
                continue;

            var sourceIdText = GetString(payload, "sourceId");
            var text = GetString(payload, "text");
            var sourceUrl = GetString(payload, "sourceUrl");
            var sourceTitle = GetString(payload, "sourceTitle");

            if (!Guid.TryParse(sourceIdText, out var sourceId)
                || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var chunkIndex = 0;

            if (payload.TryGetProperty("chunkIndex", out var indexElement))
            {
                indexElement.TryGetInt32(out chunkIndex);
            }

            var score = 0.0;

            if (item.TryGetProperty("score", out var scoreElement)
                && scoreElement.TryGetDouble(out var parsedScore))
            {
                score = parsedScore;
            }

            chunks.Add(new ResearchChunkSearchResultDto(
                sourceId,
                sourceTitle,
                sourceUrl,
                chunkIndex,
                text,
                score));
        }

        Console.WriteLine(
            $"Qdrant araması tamamlandı. ResearchId={researchId}, " +
            $"Bulunan chunk={chunks.Count}");

        return chunks;
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return string.Empty;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }
}
