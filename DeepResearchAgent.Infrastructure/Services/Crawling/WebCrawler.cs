using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using HtmlAgilityPack;

namespace DeepResearchAgent.Infrastructure.Services.Crawling;

public class WebCrawler : ICrawler
{
    private readonly HttpClient _httpClient;

    public WebCrawler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CrawlResultDto> CrawlAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            url,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var contentType =
            response.Content.Headers.ContentType?.MediaType;

        if (contentType is not null &&
            !contentType.Contains(
                "html",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Desteklenmeyen content type: {contentType}");
        }

        var html = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var document = new HtmlDocument();
        document.LoadHtml(html);

        RemoveUnnecessaryNodes(document);

        var title = ExtractTitle(document);
        var canonicalUrl = ExtractCanonicalUrl(document, url);
        var doi = ExtractDoi(document);

        var contentNode = FindMainContent(document);

        var content = CleanText(
            contentNode?.InnerText
            ?? document.DocumentNode.InnerText);

        return new CrawlResultDto
        {
            Content = content,
            Title = title,
            CanonicalUrl = canonicalUrl,
            Doi = doi
        };
    }

    private static void RemoveUnnecessaryNodes(
        HtmlDocument document)
    {
        var nodes = document.DocumentNode.SelectNodes(
            "//script|//style|//noscript|//svg|//nav|//footer|//header|//aside");

        if (nodes is null)
            return;

        foreach (var node in nodes.ToList())
            node.Remove();
    }

    private static HtmlNode? FindMainContent(
        HtmlDocument document)
    {
        var article = document.DocumentNode.SelectSingleNode("//article");

        if (article is not null)
            return article;

        var main = document.DocumentNode.SelectSingleNode("//main");

        if (main is not null)
            return main;

        return document.DocumentNode.SelectSingleNode(
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' content ')]");
    }

    private static string? ExtractTitle(HtmlDocument document)
    {
        var titleNode = document.DocumentNode.SelectSingleNode(
            "//meta[@property='og:title']/@content");

        var title = titleNode?.GetAttributeValue("content", null);

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
            : CleanText(HtmlEntity.DeEntitize(title));
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
                out var absoluteUri))
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
