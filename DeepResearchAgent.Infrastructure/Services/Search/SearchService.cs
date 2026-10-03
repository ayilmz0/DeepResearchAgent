using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DeepResearchAgent.Infrastructure.Services.Search;

public class SearchService : IResearchSearcher
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SearchService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Tavily:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Tavily API key bulunamadı.");
        }

        var request = new TavilySearchRequest
        {
            ApiKey = apiKey,
            Query = query
        };

        var response = await _httpClient.PostAsJsonAsync(
            "search",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content.ReadFromJsonAsync<TavilySearchResponse>(
                cancellationToken: cancellationToken);

        if (result?.Results is null)
        {
            return [];
        }

        return result.Results
            .Select(x => new SearchResultDto
            {
                Title = x.Title ?? string.Empty,
                Url = x.Url ?? string.Empty,
                Snippet = x.Content
            })
            .ToList();
    }

    private class TavilySearchRequest
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;
    }

    private class TavilySearchResponse
    {
        [JsonPropertyName("results")]
        public List<TavilyResult> Results { get; set; } = [];
    }

    private class TavilyResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}