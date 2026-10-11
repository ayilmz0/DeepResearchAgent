using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeepResearchAgent.Infrastructure.Services.Search;

public class SearchService : IResearchSearcher
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SearchService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException(
                "Arama sorgusu boş olamaz.",
                nameof(query));
        }

        var apiKey = _configuration["Tavily:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Tavily API key bulunamadı.");
        }

        _logger.LogInformation(
            "Tavily araması başladı. Query: {Query}",
            query);

        var request = new TavilySearchRequest
        {
            ApiKey = apiKey,
            Query = query
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "search",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            _logger.LogError(
                "Tavily API hatası. StatusCode: {StatusCode}, Query: {Query}, Response: {Response}",
                (int)response.StatusCode,
                query,
                errorBody);

            response.EnsureSuccessStatusCode();
        }

        var result =
            await response.Content.ReadFromJsonAsync<TavilySearchResponse>(
                cancellationToken: cancellationToken);

        if (result?.Results is null || result.Results.Count == 0)
        {
            _logger.LogWarning(
                "Tavily sonuç döndürmedi. Query: {Query}",
                query);

            return [];
        }

        _logger.LogInformation(
            "Tavily {Count} sonuç döndürdü. Query: {Query}",
            result.Results.Count,
            query);

        for (var i = 0; i < result.Results.Count; i++)
        {
            var item = result.Results[i];

            _logger.LogInformation(
                """
                Tavily sonucu #{Index}
                Query: {Query}
                Title: {Title}
                URL: {Url}
                Content: {Content}
                """,
                i + 1,
                query,
                item.Title,
                item.Url,
                item.Content);
        }

        return result.Results
            .Select(x => new SearchResultDto
            {
                Title = x.Title ?? string.Empty,
                Url = x.Url ?? string.Empty,
                Snippet = x.Content ?? string.Empty
            })
            .ToList();
    }

    private sealed class TavilySearchRequest
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;
    }

    private sealed class TavilySearchResponse
    {
        [JsonPropertyName("results")]
        public List<TavilyResult> Results { get; set; } = [];
    }

    private sealed class TavilyResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
