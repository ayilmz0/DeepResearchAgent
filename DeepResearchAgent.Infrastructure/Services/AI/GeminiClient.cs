using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DeepResearchAgent.Infrastructure.Services.AI;

public class GeminiClient : IAIClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private const int MaxRetries = 2;

    public GeminiClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> GenerateAsync(
        string prompt,
        object? responseSchema = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey =
            _configuration["Gemini:ApiKey"];

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

        var generationConfig =
            new Dictionary<string, object?>
            {
                ["responseMimeType"] = "application/json"
            };

        if (responseSchema is not null)
        {
            generationConfig["responseSchema"] =
                responseSchema;
        }

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

            generationConfig
        };

        var json =
            JsonSerializer.Serialize(requestBody);

        for (var attempt = 1;
             attempt <= MaxRetries + 1;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine();
            Console.WriteLine(
                $"Gemini request gönderiliyor. " +
                $"Attempt: {attempt}/{MaxRetries + 1}");

            using var content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url);

            request.Headers.Add(
                "x-goog-api-key",
                apiKey);

            request.Content = content;

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            var responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            // =====================================================
            // SUCCESS
            // =====================================================

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    "Gemini request başarılı.");

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

            // =====================================================
            // 429 - RATE LIMIT / QUOTA
            // =====================================================

            if ((int)response.StatusCode == 429)
            {
                Console.WriteLine(
                    "Gemini 429 TooManyRequests döndürdü.");

                Console.WriteLine(
                    $"Response: {responseBody}");

                // Günlük quota bittiyse retry yapmak anlamsız.
                if (IsDailyQuotaExceeded(responseBody))
                {
                    throw new HttpRequestException(
                        $"Gemini günlük quota aşıldı. " +
                        $"Response: {responseBody}");
                }

                if (attempt <= MaxRetries)
                {
                    var delay =
                        GetRetryDelay(
                            responseBody,
                            attempt);

                    Console.WriteLine(
                        $"Geçici rate limit. " +
                        $"{delay.TotalSeconds:F1} saniye beklenecek.");

                    await Task.Delay(
                        delay,
                        cancellationToken);

                    continue;
                }

                throw new HttpRequestException(
                    $"Gemini rate limit hatası. " +
                    $"Response: {responseBody}");
            }

            // =====================================================
            // 503 - SERVICE UNAVAILABLE
            // =====================================================

            if ((int)response.StatusCode == 503)
            {
                Console.WriteLine(
                    "Gemini 503 ServiceUnavailable döndürdü.");

                Console.WriteLine(
                    $"Response: {responseBody}");

                if (attempt <= MaxRetries)
                {
                    var delay =
                        GetRetryDelay(
                            responseBody,
                            attempt);

                    Console.WriteLine(
                        $"503 retry için " +
                        $"{delay.TotalSeconds:F1} saniye beklenecek.");

                    await Task.Delay(
                        delay,
                        cancellationToken);

                    continue;
                }

                throw new HttpRequestException(
                    $"Gemini 503 ServiceUnavailable. " +
                    $"Response: {responseBody}");
            }

            // =====================================================
            // OTHER ERRORS
            // =====================================================

            Console.WriteLine(
                $"Gemini API hatası: " +
                $"{(int)response.StatusCode} " +
                $"{response.StatusCode}");

            throw new HttpRequestException(
                $"Gemini API hatası. " +
                $"Status: {(int)response.StatusCode} " +
                $"{response.StatusCode}. " +
                $"Response: {responseBody}");
        }

        throw new InvalidOperationException(
            "Gemini request beklenmeyen şekilde sona erdi.");
    }

    // =============================================================
    // DAILY QUOTA CONTROL
    // =============================================================

    private static bool IsDailyQuotaExceeded(
        string responseBody)
    {
        return
            responseBody.Contains(
                "PerDay",
                StringComparison.OrdinalIgnoreCase)
            ||
            responseBody.Contains(
                "daily",
                StringComparison.OrdinalIgnoreCase)
            ||
            responseBody.Contains(
                "quota exceeded",
                StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================
    // RETRY DELAY
    // =============================================================

    private static TimeSpan GetRetryDelay(
        string responseBody,
        int attempt)
    {
        try
        {
            using var document =
                JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty(
                    "error",
                    out var error) &&
                error.TryGetProperty(
                    "details",
                    out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty(
                            "retryDelay",
                            out var retryDelayElement))
                    {
                        continue;
                    }

                    var retryDelay =
                        retryDelayElement.GetString();

                    if (string.IsNullOrWhiteSpace(
                            retryDelay))
                    {
                        continue;
                    }

                    if (retryDelay.EndsWith("s") &&
                        double.TryParse(
                            retryDelay[..^1],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var seconds))
                    {
                        // Gemini örneğin 10s diyorsa
                        // 11 saniye bekle.
                        return TimeSpan.FromSeconds(
                            seconds + 1);
                    }
                }
            }
        }
        catch (JsonException)
        {
            Console.WriteLine(
                "Gemini retry response JSON olarak parse edilemedi.");
        }

        // Gemini retryDelay göndermediyse
        // exponential backoff kullanıyoruz.
        //
        // attempt = 1 -> 4 saniye
        // attempt = 2 -> 8 saniye
        //
        // Maksimum 30 saniye.
        var fallbackSeconds =
            Math.Pow(2, attempt) * 2;

        return TimeSpan.FromSeconds(
            Math.Min(
                fallbackSeconds,
                30));
    }
}