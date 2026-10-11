
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DeepResearchAgent.Infrastructure.Services.AI;

public class GeminiClient : IAIClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private const int MaxRetries = 3;
    private const int MaxFallbackDelaySeconds = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var apiKey = _configuration["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key bulunamadı.");
        }

        var model = _configuration["Gemini:Model"]
                    ?? "gemini-3.5-flash-lite";

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var generationConfig = new Dictionary<string, object?>
        {
            ["responseMimeType"] = "application/json"
        };

        if (responseSchema is not null)
        {
            generationConfig["responseSchema"] = responseSchema;
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig
        };

        var json = JsonSerializer.Serialize(requestBody);

        for (var attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine(
                $"Gemini isteği gönderiliyor. " +
                $"Model: {model}, Deneme: {attempt}/{MaxRetries + 1}");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                url);

            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ExtractGeneratedText(responseBody);
            }

            var statusCode = (int)response.StatusCode;

            Console.WriteLine(
                $"Gemini API hatası: {statusCode} " +
                $"{response.StatusCode}");

            Console.WriteLine(
                $"Gemini hata yanıtı: {responseBody}");

            // 429: rate limit veya kota hatası.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (IsDailyQuotaExceeded(responseBody))
                {
                    throw CreateApiException(
                        "Gemini günlük kota sınırına ulaşıldı.",
                        response.StatusCode,
                        responseBody);
                }

                if (IsQuotaExhaustedWithoutRetry(responseBody))
                {
                    throw CreateApiException(
                        "Gemini kotası tükenmiş olabilir. " +
                        "Kota ve faturalandırma ayarlarını kontrol edin.",
                        response.StatusCode,
                        responseBody);
                }

                if (attempt <= MaxRetries)
                {
                    var delay = GetRetryDelay(
                        response,
                        responseBody,
                        attempt);

                    Console.WriteLine(
                        $"Gemini 429: {delay.TotalSeconds:F1} " +
                        "saniye sonra tekrar denenecek.");

                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                throw CreateApiException(
                    "Gemini rate limit hatası: yeniden deneme sınırı aşıldı.",
                    response.StatusCode,
                    responseBody);
            }

            // 503: servis geçici olarak kullanılamıyor.
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                if (attempt <= MaxRetries)
                {
                    var delay = GetRetryDelay(
                        response,
                        responseBody,
                        attempt);

                    Console.WriteLine(
                        $"Gemini 503: {delay.TotalSeconds:F1} " +
                        "saniye sonra tekrar denenecek.");

                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                throw CreateApiException(
                    "Gemini servisi geçici olarak kullanılamıyor.",
                    response.StatusCode,
                    responseBody);
            }

            // Diğer HTTP hataları otomatik olarak tekrarlanmaz.
            throw CreateApiException(
                "Gemini API isteği başarısız oldu.",
                response.StatusCode,
                responseBody);
        }

        throw new InvalidOperationException(
            "Gemini isteği beklenmeyen şekilde sona erdi.");
    }

    private static string ExtractGeneratedText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);

        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Gemini yanıtında candidates bulunamadı. " +
                $"Yanıt: {responseBody}");
        }

        var candidate = candidates[0];

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Gemini yanıtında içerik bulunamadı.");
        }

        var textBuilder = new StringBuilder();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    textBuilder.Append(text);
                }
            }
        }

        var result = textBuilder.ToString();

        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException(
                "Gemini boş veya metin içermeyen bir yanıt döndürdü.");
        }

        return result;
    }

    private static bool IsDailyQuotaExceeded(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (!document.RootElement.TryGetProperty(
                    "error", out var error) ||
                !error.TryGetProperty("details", out var details) ||
                details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty(
                        "quotaId", out var quotaIdElement))
                {
                    continue;
                }

                var quotaId = quotaIdElement.GetString();

                if (string.IsNullOrWhiteSpace(quotaId))
                {
                    continue;
                }

                if (quotaId.Contains(
                        "PerDay",
                        StringComparison.OrdinalIgnoreCase) ||
                    quotaId.Contains(
                        "RequestsPerDay",
                        StringComparison.OrdinalIgnoreCase) ||
                    quotaId.Contains(
                        "TokensPerDay",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // JSON okunamazsa günlük kota olduğu varsayılmaz.
        }

        return false;
    }

    private static bool IsQuotaExhaustedWithoutRetry(
        string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (!document.RootElement.TryGetProperty(
                    "error", out var error) ||
                !error.TryGetProperty("details", out var details) ||
                details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty(
                        "@type", out var typeElement))
                {
                    continue;
                }

                var type = typeElement.GetString();

                if (type != "type.googleapis.com/google.rpc.QuotaFailure")
                {
                    continue;
                }

                if (!detail.TryGetProperty(
                        "violations", out var violations) ||
                    violations.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var violation in violations.EnumerateArray())
                {
                    if (!violation.TryGetProperty(
                            "quotaId", out var quotaIdElement))
                    {
                        continue;
                    }

                    var quotaId = quotaIdElement.GetString();

                    if (string.IsNullOrWhiteSpace(quotaId))
                    {
                        continue;
                    }

                    // PerDay hataları ayrı metotta işleniyor.
                    if (quotaId.Contains(
                            "PerDay",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Dakikalık limitler retry edilebilir.
                    if (quotaId.Contains(
                            "PerMinute",
                            StringComparison.OrdinalIgnoreCase) ||
                        quotaId.Contains(
                            "RPM",
                            StringComparison.OrdinalIgnoreCase) ||
                        quotaId.Contains(
                            "TPM",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Bilinmeyen limitler için kotanın kesin
                    // tükenmiş olduğu varsayılmıyor.
                }
            }
        }
        catch (JsonException)
        {
            // Yanıt ayrıştırılamazsa yeniden deneme politikasını
            // HTTP 429 ve retry sayısı belirler.
        }

        return false;
    }

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        string responseBody,
        int attempt)
    {
        // Önce HTTP Retry-After başlığını kontrol et.
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is TimeSpan delta &&
            delta > TimeSpan.Zero)
        {
            return delta + TimeSpan.FromMilliseconds(500);
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            var remaining = date - DateTimeOffset.UtcNow;

            if (remaining > TimeSpan.Zero)
            {
                return remaining + TimeSpan.FromMilliseconds(500);
            }
        }

        // Ardından Gemini'nin RetryInfo ayrıntısını kontrol et.
        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty(
                    "error", out var error) &&
                error.TryGetProperty(
                    "details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty(
                            "@type", out var typeElement) ||
                        typeElement.GetString() !=
                        "type.googleapis.com/google.rpc.RetryInfo")
                    {
                        continue;
                    }

                    if (!detail.TryGetProperty(
                            "retryDelay", out var delayElement))
                    {
                        continue;
                    }

                    var retryDelay = delayElement.GetString();

                    if (TryParseRetryDelay(retryDelay, out var seconds))
                    {
                        return TimeSpan.FromSeconds(seconds + 0.5);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // JSON okunamazsa exponential backoff kullanılır.
        }

        // Sunucu süre belirtmezse exponential backoff.
        var fallbackSeconds = Math.Min(
            Math.Pow(2, attempt),
            MaxFallbackDelaySeconds);

        return TimeSpan.FromSeconds(fallbackSeconds);
    }

    private static bool TryParseRetryDelay(
        string? retryDelay,
        out double seconds)
    {
        seconds = 0;

        if (string.IsNullOrWhiteSpace(retryDelay) ||
            !retryDelay.EndsWith('s'))
        {
            return false;
        }

        return double.TryParse(
            retryDelay[..^1],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out seconds) && seconds >= 0;
    }

    private static HttpRequestException CreateApiException(
        string message,
        HttpStatusCode statusCode,
        string responseBody)
    {
        return new HttpRequestException(
            $"{message} HTTP {(int)statusCode} ({statusCode}). " +
            $"Response: {responseBody}",
            inner: null,
            statusCode);
    }
}
