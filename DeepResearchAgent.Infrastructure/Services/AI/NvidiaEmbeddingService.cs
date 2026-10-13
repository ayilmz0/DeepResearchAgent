using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DeepResearchAgent.Infrastructure.Services.AI;

public sealed class NvidiaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public NvidiaEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;

        _apiKey = configuration["Nvidia:ApiKey"]
            ?? throw new InvalidOperationException(
                "Nvidia:ApiKey yapılandırılmamış.");

        _model = configuration["Nvidia:EmbeddingModel"]
            ?? "nvidia/llama-nemotron-embed-vl-1b-v2";

        // Embedding servisi NVIDIA adresini kullanmalı.
        _httpClient.BaseAddress ??=
            new Uri("https://integrate.api.nvidia.com/");

        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string inputType,
        CancellationToken cancellationToken = default)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }

        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        if (inputType is not ("query" or "passage"))
        {
            throw new ArgumentException(
                "inputType yalnızca 'query' veya 'passage' olabilir.",
                nameof(inputType));
        }

        var requestBody = new
        {
            input = texts,
            model = _model,
            input_type = inputType,
            encoding_format = "float",
            truncate = "NONE"
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/embeddings");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);

        request.Content = JsonContent.Create(requestBody);

        // Hata ayıklamak için gerçek hedef adresi logla.
        var requestUri = request.RequestUri is { IsAbsoluteUri: true } absoluteUri
            ? absoluteUri
            : new Uri(_httpClient.BaseAddress!, request.RequestUri!);

        Console.WriteLine($"NVIDIA Embedding URL: {requestUri}");
        Console.WriteLine($"NVIDIA Embedding Model: {_model}");
        Console.WriteLine($"Embedding input type: {inputType}");
        Console.WriteLine($"Embedding metin sayısı: {texts.Count}");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"NVIDIA embedding isteği başarısız. " +
                $"HTTP {(int)response.StatusCode} ({response.StatusCode}). " +
                $"URL: {requestUri}. Yanıt: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty(
                "data", out var dataElement)
            || dataElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "NVIDIA embedding yanıtında 'data' dizisi bulunamadı.");
        }

        var indexedEmbeddings = new List<(int Index, float[] Embedding)>();

        foreach (var item in dataElement.EnumerateArray())
        {
            if (!item.TryGetProperty("index", out var indexElement)
                || !indexElement.TryGetInt32(out var index))
            {
                throw new InvalidOperationException(
                    "Embedding yanıtındaki bir öğenin index değeri geçersiz.");
            }

            if (!item.TryGetProperty(
                    "embedding", out var embeddingElement)
                || embeddingElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Embedding yanıtında {index} indeksli öğenin vektörü bulunamadı.");
            }

            var vector = new List<float>();

            foreach (var number in embeddingElement.EnumerateArray())
            {
                if (!number.TryGetSingle(out var value))
                {
                    throw new InvalidOperationException(
                        $"Embedding vektöründe geçersiz sayı var. Index: {index}");
                }

                vector.Add(value);
            }

            if (vector.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Boş embedding vektörü döndü. Index: {index}");
            }

            indexedEmbeddings.Add((index, vector.ToArray()));
        }

        if (indexedEmbeddings.Count != texts.Count)
        {
            throw new InvalidOperationException(
                $"Embedding sayısı uyuşmuyor. " +
                $"Gönderilen: {texts.Count}, " +
                $"Dönen: {indexedEmbeddings.Count}.");
        }

        var orderedEmbeddings = indexedEmbeddings
            .OrderBy(item => item.Index)
            .ToList();

        for (var i = 0; i < orderedEmbeddings.Count; i++)
        {
            if (orderedEmbeddings[i].Index != i)
            {
                throw new InvalidOperationException(
                    $"Embedding index sırası eksik veya hatalı. Beklenen: {i}, " +
                    $"Gelen: {orderedEmbeddings[i].Index}.");
            }
        }

        var expectedDimension = orderedEmbeddings[0].Embedding.Length;

        if (orderedEmbeddings.Any(
                item => item.Embedding.Length != expectedDimension))
        {
            throw new InvalidOperationException(
                "Dönen embedding vektörlerinin boyutları birbiriyle uyuşmuyor.");
        }

        Console.WriteLine(
            $"Embedding başarılı. Vektör boyutu: {expectedDimension}");

        return orderedEmbeddings
            .Select(item => item.Embedding)
            .ToArray();
    }
}