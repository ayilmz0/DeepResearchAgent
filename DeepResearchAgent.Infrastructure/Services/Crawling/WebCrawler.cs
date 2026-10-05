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

    public async Task<string> CrawlAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            url,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} - " +
                $"{response.StatusCode}");
        }

        var contentType =
            response.Content.Headers.ContentType?.MediaType;

        if (contentType is not null &&
            !contentType.Contains("html"))
        {
            throw new InvalidOperationException(
                $"Desteklenmeyen content type: {contentType}");
        }

        var html = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var document = new HtmlDocument();

        document.LoadHtml(html);

        RemoveUnnecessaryNodes(document);

        var contentNode = FindMainContent(document);

        if (contentNode is null)
        {
            return CleanText(
                document.DocumentNode.InnerText);
        }

        return CleanText(
            contentNode.InnerText);
    }

    private static void RemoveUnnecessaryNodes(
        HtmlDocument document)
    {
        var nodes = document.DocumentNode.SelectNodes(
            "//script" +
            "|//style" +
            "|//noscript" +
            "|//svg" +
            "|//nav" +
            "|//footer" +
            "|//header" +
            "|//aside");

        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes)
        {
            node.Remove();
        }
    }

    private static HtmlNode? FindMainContent(
        HtmlDocument document)
    {
        var article = document.DocumentNode.SelectSingleNode(
            "//article");

        if (article is not null)
        {
            return article;
        }

        var main = document.DocumentNode.SelectSingleNode(
            "//main");

        if (main is not null)
        {
            return main;
        }

        var content = document.DocumentNode.SelectSingleNode(
            "//*[contains(@class, 'content')]");

        if (content is not null)
        {
            return content;
        }

        return null;
    }

    private static string CleanText(string text)
    {
        return string.Join(
            " ",
            text.Split(
                [' ', '\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries));
    }
}