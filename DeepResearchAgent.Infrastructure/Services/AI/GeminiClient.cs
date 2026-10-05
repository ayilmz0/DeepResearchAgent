using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace DeepResearchAgent.Infrastructure.Services.AI;

public class GeminiClient : IAIClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public GeminiClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key bulunamadı.");
        }

        var model =
            _configuration["Gemini:Model"]
            ?? "gemini-3.6-flash";

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new
                        {
                            text = prompt
                        }
                    }
                }
            },

            generationConfig = new
            {
                responseMimeType = "application/json",

                responseSchema = new
                {
                    type = "ARRAY",

                    items = new
                    {
                        type = "OBJECT",

                        properties = new
                        {
                            claim = new
                            {
                                type = "STRING"
                            },

                            value = new
                            {
                                type = "STRING",
                                nullable = true
                            },

                            confidence = new
                            {
                                type = "NUMBER"
                            }
                        },

                        required = new[]
                        {
                            "claim",
                            "value",
                            "confidence"
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.Add(
            "x-goog-api-key",
            apiKey);

        request.Content = content;

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        var responseBody =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini API hatası. " +
                $"Status: {(int)response.StatusCode} " +
                $"{response.StatusCode}. " +
                $"Response: {responseBody}");
        }

        using var document =
            JsonDocument.Parse(responseBody);

        var text =
            document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "Gemini boş cevap döndürdü.");
        }

        return text;
    }
}