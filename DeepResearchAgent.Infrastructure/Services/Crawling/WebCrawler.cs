
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using HtmlAgilityPack;
using System.Net;

namespace DeepResearchAgent.Infrastructure.Services.Crawling;

public class WebCrawler : ICrawler
{
    private const int MaxRedirects = 5;
    private const int MaxContentBytes = 5 * 1024 * 1024;

    private readonly HttpClient _httpClient;

    public WebCrawler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CrawlResultDto> CrawlAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        // İstek başlamadan önce iptali kontrol et.
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var currentUri))
        {
            throw new InvalidOperationException("Geçersiz URL.");
        }

        // Yalnızca HTTP ve HTTPS desteklenir.
        if (currentUri.Scheme != Uri.UriSchemeHttp &&
            currentUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Yalnızca HTTP veya HTTPS adresleri desteklenir.");
        }

        for (var redirectCount = 0; ; redirectCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Her yönlendirmede hedef URL'yi yeniden doğrula.
            await CrawlerUrlValidator.ValidateAsync(
                currentUri.AbsoluteUri,
                cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                currentUri);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaxRedirects)
                {
                    throw new InvalidOperationException(
                        "İzin verilen yönlendirme sayısı aşıldı.");
                }

                var location = response.Headers.Location;

                if (location is null)
                {
                    throw new InvalidOperationException(
                        "Yönlendirme yanıtında Location başlığı bulunamadı.");
                }

                if (!Uri.TryCreate(currentUri, location, out var nextUri))
                {
                    throw new InvalidOperationException(
                        "Geçersiz yönlendirme adresi.");
                }

                if (nextUri.Scheme != Uri.UriSchemeHttp &&
                    nextUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException(
                        "Yönlendirme yalnızca HTTP veya HTTPS adresine yapılabilir.");
                }

                currentUri = nextUri;
                continue;
            }

            response.EnsureSuccessStatusCode();

            var contentType =
                response.Content.Headers.ContentType?.MediaType;

            if (contentType is null ||
                (!contentType.Contains(
                     "html",
                     StringComparison.OrdinalIgnoreCase) &&
                 !contentType.Contains(
                     "xhtml",
                     StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Desteklenmeyen veya belirtilmemiş content type: {contentType ?? "bilinmiyor"}");
            }

            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength > MaxContentBytes)
            {
                throw new InvalidOperationException(
                    "HTML yanıtı izin verilen 5 MB sınırını aşıyor.");
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using var memoryStream = new MemoryStream();

            var buffer = new byte[81920];
            var totalBytesRead = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesRead = await responseStream.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

                if (bytesRead == 0)
                    break;

                totalBytesRead += bytesRead;

                if (totalBytesRead > MaxContentBytes)
                {
                    throw new InvalidOperationException(
                        "HTML yanıtı izin verilen 5 MB sınırını aşıyor.");
                }

                await memoryStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            memoryStream.Position = 0;

            var document = new HtmlDocument();
            document.Load(memoryStream);

            RemoveUnnecessaryNodes(document);

            var title = ExtractTitle(document);

            var canonicalUrl = ExtractCanonicalUrl(
                document,
                currentUri.AbsoluteUri);

            var doi = ExtractDoi(document);
            var contentNode = FindMainContent(document);

            var content = CleanText(
                contentNode?.InnerText
                ?? document.DocumentNode.InnerText);

            cancellationToken.ThrowIfCancellationRequested();

            return new CrawlResultDto
            {
                Content = content,
                Title = title,
                CanonicalUrl = canonicalUrl,
                Doi = doi
            };
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
    }

    private static void RemoveUnnecessaryNodes(
        HtmlDocument document)
    {
        var nodes = document.DocumentNode.SelectNodes(
            "//script|//style|//noscript|//svg|//nav|//footer|//header|//aside");

        if (nodes is null)
            return;

        foreach (var node in nodes.ToList())
        {
            node.Remove();
        }
    }

    private static HtmlNode? FindMainContent(
        HtmlDocument document)
    {
        var article = document.DocumentNode
            .SelectSingleNode("//article");

        if (article is not null)
            return article;

        var main = document.DocumentNode
            .SelectSingleNode("//main");

        if (main is not null)
            return main;

        return document.DocumentNode.SelectSingleNode(
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' content ')]");
    }

    private static string? ExtractTitle(HtmlDocument document)
    {
        var title = document.DocumentNode
            .SelectSingleNode("//meta[@property='og:title']")
            ?.GetAttributeValue("content", null);

        if (string.IsNullOrWhiteSpace(title))
        {
            title = document.DocumentNode
                .SelectSingleNode("//title")
                ?.InnerText;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = document.DocumentNode
                .SelectSingleNode("//h1")
                ?.InnerText;
        }

        return string.IsNullOrWhiteSpace(title)
            ? null
            : CleanText(title);
    }

    private static string? ExtractCanonicalUrl(
        HtmlDocument document,
        string originalUrl)
    {
        var canonicalNode = document.DocumentNode.SelectSingleNode(
            "//link[translate(@rel,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='canonical']");

        var canonical = canonicalNode?.GetAttributeValue(
            "href",
            null);

        if (string.IsNullOrWhiteSpace(canonical))
            return originalUrl;

        if (Uri.TryCreate(
                new Uri(originalUrl),
                canonical,
                out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp ||
             absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.AbsoluteUri;
        }

        return originalUrl;
    }

    private static string? ExtractDoi(HtmlDocument document)
    {
        var doiNode = document.DocumentNode.SelectSingleNode(
            "//meta[translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='citation_doi']");

        var doi = doiNode?.GetAttributeValue("content", null);

        if (string.IsNullOrWhiteSpace(doi))
        {
            doiNode = document.DocumentNode.SelectSingleNode(
                "//meta[translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='dc.identifier']");

            doi = doiNode?.GetAttributeValue("content", null);
        }

        if (string.IsNullOrWhiteSpace(doi))
            return null;

        doi = doi.Trim();

        if (doi.StartsWith(
                "https://doi.org/",
                StringComparison.OrdinalIgnoreCase))
        {
            doi = doi["https://doi.org/".Length..];
        }
        else if (doi.StartsWith(
                     "http://doi.org/",
                     StringComparison.OrdinalIgnoreCase))
        {
            doi = doi["http://doi.org/".Length..];
        }
        else if (doi.StartsWith(
                     "doi:",
                     StringComparison.OrdinalIgnoreCase))
        {
            doi = doi["doi:".Length..].Trim();
        }

        return doi;
    }

    private static string CleanText(string text)
    {
        return string.Join(
            " ",
            HtmlEntity.DeEntitize(text).Split(
                [' ', '\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries));
    }
}
