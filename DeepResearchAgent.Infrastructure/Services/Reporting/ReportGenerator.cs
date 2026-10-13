
using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace DeepResearchAgent.Infrastructure.Services.Reporting;

public sealed class ReportGenerator : IReportGenerator
{
    private readonly IAIClient _aiClient;
    private readonly IResearchChunkRetriever _chunkRetriever;
    private readonly ILogger<ReportGenerator> _logger;

    private const int RetrievalTopK = 10;
    private const int MaxFacts = 100;
    private const int MaxChunkCharacters = 3000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string" },
            content = new { type = "string" }
        },
        required = new[] { "title", "content" },
        additionalProperties = false
    };

    public ReportGenerator(
        IAIClient aiClient,
        IResearchChunkRetriever chunkRetriever,
        ILogger<ReportGenerator> logger)
    {
        _aiClient = aiClient;
        _chunkRetriever = chunkRetriever;
        _logger = logger;
    }

    public async Task<Report> GenerateAsync(
        Research research,
        IReadOnlyList<Fact> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(facts);

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Rapor üretimi başladı. ResearchId: {ResearchId}, FactCount: {FactCount}",
            research.Id,
            facts.Count);

        // 1. Araştırma konusuyla ilgili RAG parçalarını getir.
        var chunks = await _chunkRetriever.SearchAsync(
            research.Id,
            research.Query,
            topK: RetrievalTopK,
            cancellationToken: cancellationToken);

        var uniqueSourceCount = chunks
            .Select(x => x.SourceId)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .Count();

        _logger.LogInformation(
            "RAG sonuçları alındı. ResearchId: {ResearchId}, " +
            "ChunkCount: {ChunkCount}, SourceCount: {SourceCount}",
            research.Id,
            chunks.Count,
            uniqueSourceCount);

        if (chunks.Count == 0 && facts.Count == 0)
        {
            throw new InvalidOperationException(
                "Rapor üretilemedi: Kullanılabilir araştırma bulgusu " +
                "veya RAG kaynak parçası bulunamadı.");
        }

        // 2. Rapor üretim promptunu hazırla.
        var prompt = BuildReportPrompt(
            research.Query,
            facts,
            chunks);

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Raporu tek AI çağrısıyla üret.
        var rawResponse = await _aiClient.GenerateAsync(
            prompt,
            ResponseSchema,
            cancellationToken);

        var generatedReport = DeserializeResponse<GeneratedReportDto>(
            rawResponse);

        if (string.IsNullOrWhiteSpace(generatedReport.Title))
        {
            throw new InvalidOperationException(
                "AI tarafından rapor başlığı oluşturulamadı.");
        }

        if (string.IsNullOrWhiteSpace(generatedReport.Content))
        {
            throw new InvalidOperationException(
                "AI tarafından rapor içeriği oluşturulamadı.");
        }

        // 4. Metodoloji ve sınırlılıkları ekle.
        var content = AppendMethodologyDisclosure(
            generatedReport.Content.Trim(),
            facts.Count,
            chunks.Count);

        // 5. Raporu veritabanına kaydedilmek üzere döndür.
        var report = new Report
        {
            Id = Guid.NewGuid(),
            ResearchId = research.Id,
            Title = generatedReport.Title.Trim(),
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Rapor üretimi tamamlandı. ResearchId: {ResearchId}, " +
            "ContentLength: {ContentLength}",
            research.Id,
            content.Length);

        return report;
    }

    private static string BuildReportPrompt(
        string researchQuery,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<ResearchChunkSearchResultDto> chunks)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Sen kapsamlı araştırma raporları hazırlayan bir analiz asistanısın.");

        builder.AppendLine();
        builder.AppendLine("ARAŞTIRMA KONUSU:");
        builder.AppendLine(researchQuery);

        builder.AppendLine();
        builder.AppendLine("AMAÇ:");
        builder.AppendLine(
            "Araştırma bulgularını ve kaynak metinlerini analiz ederek " +
            "düzenli, tutarlı, bilgilendirici ve tekrarsız bir rapor oluştur.");

        builder.AppendLine();
        builder.AppendLine("İÇERİK KURALLARI:");

        builder.AppendLine(
            "1. Yalnızca verilen bulgulara ve kaynak metinlerine dayanan " +
            "olgusal bilgiler kullan.");

        builder.AppendLine(
            "2. Kaynaklarda bulunmayan istatistik, yüzde, tarih, araştırma " +
            "sonucu, şirket uygulaması veya piyasa tahmini uydurma.");

        builder.AppendLine(
            "3. Bir bilginin dayanağı yoksa onu kesin bir gerçek olarak sunma. " +
            "Gerekirse bilgi eksikliğini açıkça belirt.");

        builder.AppendLine(
            "4. Aynı bilgiyi farklı cümlelerle tekrarlama. Birbiriyle örtüşen " +
            "bulguları birleştir.");

        builder.AppendLine(
            "5. Kaynakların desteklemediği gelecek tahminlerini kesin " +
            "gerçekler gibi yazma.");

        builder.AppendLine(
            "6. Kaynaklar arasında bağımsız doğrulama yapılmış gibi davranma.");

        builder.AppendLine(
            "7. Tek bir kaynaktaki iddiayı bütün sektör için geçerli " +
            "bir sonuç olarak genelleme.");

        builder.AppendLine(
            "8. Konuyla doğrudan ilgili olmayan bilgileri rapora ekleme.");

        builder.AppendLine(
            "9. Teknik terimleri gerektiğinde kısa ve anlaşılır biçimde açıkla.");

        builder.AppendLine(
            "10. Raporu sadece madde listesine dönüştürme. Bulguların " +
            "birbirleriyle ilişkilerini ve pratik anlamlarını da analiz et.");

        builder.AppendLine(
            "11. Bilgi eksikse bölümü yapay bilgilerle doldurma.");

        builder.AppendLine(
            "12. Metin içinde [S1], [S2] gibi kaynak atıfları oluşturma. " +
            "Ayrı bir kaynakça bölümü de üretme.");

        builder.AppendLine();
        builder.AppendLine("RAPOR YAPISI:");

        builder.AppendLine(
            "## 1. Yönetici Özeti\n" +
            "Araştırmanın temel bulgularını kısa ve anlaşılır biçimde özetle.");

        builder.AppendLine(
            "## 2. Genel Bakış\n" +
            "Konunun kapsamını ve sektördeki önemini açıkla.");

        builder.AppendLine(
            "## 3. Temel Bulgular ve Kullanım Alanları\n" +
            "Bulguları anlamlı alt başlıklar altında topla. Tekrarları kaldır.");

        builder.AppendLine(
            "## 4. Fırsatlar ve Potansiyel Faydalar\n" +
            "Bulgularla desteklenen faydaları ve olası kullanım senaryolarını açıkla.");

        builder.AppendLine(
            "## 5. Riskler ve Uygulama Zorlukları\n" +
            "Kaynaklarda veya araştırma bulgularında yer alan sınırlamaları ele al.");

        builder.AppendLine(
            "## 6. Geleceğe Yönelik Değerlendirme\n" +
            "Yalnızca mevcut bulguların desteklediği çıkarımları yap. " +
            "Çıkarımları doğrulanmış gerçeklerden ayır.");

        builder.AppendLine(
            "## 7. Sonuç ve Öneriler\n" +
            "Araştırmayı sentezle ve bulgularla uyumlu, uygulanabilir öneriler sun.");

        builder.AppendLine();
        builder.AppendLine("ARAŞTIRMA BULGULARI:");

        if (facts.Count == 0)
        {
            builder.AppendLine(
                "Yapılandırılmış araştırma bulgusu bulunmuyor. " +
                "Yalnızca aşağıdaki kaynak parçalarından yararlan.");
        }
        else
        {
            foreach (var fact in facts.Take(MaxFacts))
            {
                builder.AppendLine($"İddia: {fact.Claim}");
                builder.AppendLine($"Değer: {fact.Value}");
                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("RAG KAYNAK METİNLERİ:");

        if (chunks.Count == 0)
        {
            builder.AppendLine(
                "Bu aramada RAG kaynak parçası bulunamadı. " +
                "Kaynak metni varmış gibi davranma.");
        }
        else
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                builder.AppendLine(
                    $"--- Kaynak parçası {i + 1} ---");

                builder.AppendLine($"Başlık: {chunk.SourceTitle}");
                builder.AppendLine($"URL: {chunk.SourceUrl}");
                builder.AppendLine($"Parça numarası: {chunk.ChunkIndex}");

                var text = chunk.Text ?? string.Empty;

                if (text.Length > MaxChunkCharacters)
                {
                    text = text[..MaxChunkCharacters];
                }

                builder.AppendLine("İçerik:");
                builder.AppendLine(text);
                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("ÇIKTI KURALLARI:");
        builder.AppendLine(
            "Yalnızca JSON döndür. JSON alanları title ve content olmalıdır. " +
            "content alanında raporu Markdown biçiminde oluştur.");

        return builder.ToString();
    }

    private static string AppendMethodologyDisclosure(
        string content,
        int factCount,
        int chunkCount)
    {
        var builder = new StringBuilder(content.Trim());

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("## Metodoloji ve Sınırlılıklar");
        builder.AppendLine();

        builder.AppendLine(
            "Bu rapor, araştırma sürecinde elde edilen yapılandırılmış " +
            "bulgular ve RAG yöntemiyle getirilen kaynak metinleri " +
            "kullanılarak hazırlanmıştır.");

        builder.AppendLine();

        builder.AppendLine(
            $"Rapor oluşturulurken {factCount} yapılandırılmış bulgu " +
            $"ve {chunkCount} kaynak metni parçası modele sunulmuştur.");

        builder.AppendLine();

        builder.AppendLine(
            "Bu sayılar, bilgilerin doğruluğunun bağımsız olarak " +
            "doğrulandığı anlamına gelmez. Raporun kapsamı ve güvenilirliği, " +
            "araştırma sırasında elde edilen verilerin niteliğiyle sınırlıdır.");

        return builder.ToString();
    }

    private static T DeserializeResponse<T>(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException(
                "AI rapor üretimi boş yanıt döndürdü.");
        }

        var json = ExtractJson(response);

        try
        {
            var result = JsonSerializer.Deserialize<T>(
                json,
                JsonOptions);

            return result ?? throw new InvalidOperationException(
                "AI yanıtı boş bir nesneye dönüştürüldü.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "AI yanıtı beklenen JSON formatında değil.",
                exception);
        }
    }

    private static string ExtractJson(string response)
    {
        var text = response.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');

            if (firstNewLine >= 0)
            {
                text = text[(firstNewLine + 1)..];
            }

            if (text.EndsWith("```", StringComparison.Ordinal))
            {
                text = text[..^3].Trim();
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            return text[start..(end + 1)];
        }

        return text;
    }

    private sealed class GeneratedReportDto
    {
        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }
}
