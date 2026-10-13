
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeepResearchAgent.Infrastructure.Services.AI;

public sealed class NvidiaAIClient : IAIClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<NvidiaAIClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NvidiaAIClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<NvidiaAIClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var apiKey = configuration["Nvidia:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Nvidia:ApiKey yapılandırmada bulunamadı.");
        }

        _model = configuration["Nvidia:ChatModel"]
            ?? "meta/llama-3.3-70b-instruct";

        _httpClient.BaseAddress ??=
            new Uri("https://integrate.api.nvidia.com/");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> GenerateAsync(
        string prompt,
        object? responseSchema = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException(
                "Prompt boş olamaz.",
                nameof(prompt));
        }

        var finalPrompt = prompt;

        if (responseSchema is not null)
        {
            var schemaJson = JsonSerializer.Serialize(
                responseSchema,
                JsonOptions);

            finalPrompt = $"""
                Yanıtını geçerli JSON olarak üret.
                JSON dışında açıklama, Markdown veya kod bloğu ekleme.
                Aşağıdaki JSON şemasına uygun bir JSON NESNESİ döndür.
                Bir JSON dizisiyle doğrudan başlama.
                Nesne gerekiyorsa sonuçları "results" alanında döndür.

                Şema:
                {schemaJson}

                İSTEK:
                {prompt}
                """;
        }


        var request = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = new[]
            {
        new
        {
            role = "user",
            content = finalPrompt
        }
    },
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["stream"] = false,

            // Nemotron'un uzun reasoning çıktısı üretmesini kapat.
            ["chat_template_kwargs"] = new
            {
                enable_thinking = false
            }
        };

        if (responseSchema is not null)
        {
            request["response_format"] = new
            {
                type = "json_object"
            };
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "v1/chat/completions",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            _logger.LogError(
                "NVIDIA API error. StatusCode={StatusCode}, Model={Model}, Body={Body}",
                (int)response.StatusCode,
                _model,
                errorBody);

            throw new HttpRequestException(
                $"NVIDIA Chat API hatası: " +
                $"{(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Yanıt: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            _logger.LogError(
                "NVIDIA response has no choices. Response={Response}",
                root.GetRawText());

            throw new InvalidOperationException(
                "NVIDIA API yanıtında choices bulunamadı.");
        }

        var choice = choices[0];

        var finishReason = choice.TryGetProperty(
            "finish_reason",
            out var finishReasonElement)
            ? finishReasonElement.ToString()
            : "unknown";

        if (!choice.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object)
        {
            _logger.LogError(
                "NVIDIA response has no message. FinishReason={FinishReason}, Response={Response}",
                finishReason,
                choice.GetRawText());

            throw new InvalidOperationException(
                $"NVIDIA API yanıtında message bulunamadı. " +
                $"finish_reason={finishReason}");
        }

        if (message.TryGetProperty("refusal", out var refusalElement) &&
            refusalElement.ValueKind == JsonValueKind.String)
        {
            var refusal = refusalElement.GetString();

            if (!string.IsNullOrWhiteSpace(refusal))
            {
                _logger.LogWarning(
                    "NVIDIA model returned a refusal. Model={Model}, Refusal={Refusal}",
                    _model,
                    refusal);

                throw new InvalidOperationException(
                    $"NVIDIA modeli isteği reddetti: {refusal}");
            }
        }

        if (!message.TryGetProperty("content", out var contentElement))
        {
            _logger.LogError(
                "NVIDIA response has no content property. Model={Model}, FinishReason={FinishReason}, Message={Message}",
                _model,
                finishReason,
                message.GetRawText());

            throw new InvalidOperationException(
                "NVIDIA API yanıtında content alanı bulunamadı. " +
                $"finish_reason={finishReason}. " +
                "Ayrıntılar loglarda.");
        }

        if (contentElement.ValueKind != JsonValueKind.String)
        {
            _logger.LogError(
                "NVIDIA yanıtı geçersiz. Model={Model}, " +
                "FinishReason={FinishReason}, ContentKind={ContentKind}",
                _model,
                finishReason,
                contentElement.ValueKind);

            throw new InvalidOperationException(
                "NVIDIA API content alanı metin türünde değil. " +
                $"ContentKind={contentElement.ValueKind}, " +
                $"finish_reason={finishReason}. Ayrıntılar loglarda.");
        }

        var result = contentElement.GetString();

        if (string.IsNullOrWhiteSpace(result))
        {
            var reasoning = message.TryGetProperty(
                "reasoning",
                out var reasoningElement)
                ? reasoningElement.ToString()
                : null;

            _logger.LogError(
                "NVIDIA returned empty content. Model={Model}, FinishReason={FinishReason}, Reasoning={Reasoning}, Message={Message}, Usage={Usage}",
                _model,
                finishReason,
                reasoning,
                message.GetRawText(),
                root.TryGetProperty("usage", out var usage)
                    ? usage.GetRawText()
                    : "unknown");

            var reason = finishReason switch
            {
                "length" =>
                    "Model token sınırına ulaştı; max_tokens artırılması gerekebilir.",

                "content_filter" =>
                    "Yanıt içerik filtresi tarafından durdurulmuş olabilir.",

                "null" or "unknown" =>
                    "API yanıtında bitiş nedeni belirtilmemiş.",

                _ =>
                    $"finish_reason={finishReason}"
            };

            throw new InvalidOperationException(
                "NVIDIA API boş metin içeriği döndürdü. " +
                $"{reason} Ayrıntılar loglarda.");
        }

        _logger.LogInformation(
            "NVIDIA generation succeeded. Model={Model}, FinishReason={FinishReason}, OutputLength={OutputLength}",
            _model,
            finishReason,
            result.Length);

        return result.Trim();
    }
}
