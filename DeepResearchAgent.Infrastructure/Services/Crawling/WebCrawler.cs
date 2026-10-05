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
        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("          CRAWLER START");
        Console.WriteLine("======================================");
        Console.WriteLine($"URL: {url}");
        Console.WriteLine("HTTP request gönderiliyor...");

        var response = await _httpClient.GetAsync(
            url,
            cancellationToken);

        Console.WriteLine(
            $"HTTP Status: {(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(
                $"HTTP request başarısız: {url}");

            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} - " +
                $"{response.StatusCode}");
        }

        var contentType =
            response.Content.Headers.ContentType?.MediaType;

        Console.WriteLine(
            $"Content-Type: {contentType}");

        if (contentType is not null &&
            !contentType.Contains("html"))
        {
            Console.WriteLine(
                $"Desteklenmeyen Content-Type: {contentType}");

            throw new InvalidOperationException(
                $"Desteklenmeyen content type: {contentType}");
        }

        Console.WriteLine(
            "HTML içeriği okunuyor...");

        var html =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        Console.WriteLine(
            $"HTML alındı. Karakter sayısı: {html.Length}");

        var document = new HtmlDocument();

        Console.WriteLine(
            "HTML parse ediliyor...");

        document.LoadHtml(html);

        Console.WriteLine(
            "Gereksiz HTML node'ları temizleniyor...");

        RemoveUnnecessaryNodes(document);

        Console.WriteLine(
            "Ana içerik aranıyor...");

        var contentNode =
            FindMainContent(document);

        string result;

        if (contentNode is null)
        {
            Console.WriteLine(
                "Ana içerik node'u bulunamadı. " +
                "Document body kullanılacak.");

            result = CleanText(
                document.DocumentNode.InnerText);
        }
        else
        {
            Console.WriteLine(
                "Ana içerik node'u bulundu.");

            result = CleanText(
                contentNode.InnerText);
        }

        Console.WriteLine(
            $"Temizlenmiş içerik uzunluğu: {result.Length}");

        Console.WriteLine(
            "Crawler başarıyla tamamlandı.");

        Console.WriteLine(
            "======================================");

        return result;
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
            Console.WriteLine(
                "Temizlenecek gereksiz node bulunamadı.");

            return;
        }

        Console.WriteLine(
            $"Temizlenecek node sayısı: {nodes.Count}");

        foreach (var node in nodes)
        {
            node.Remove();
        }
    }

    private static HtmlNode? FindMainContent(
        HtmlDocument document)
    {
        var article =
            document.DocumentNode.SelectSingleNode(
                "//article");

        if (article is not null)
        {
            Console.WriteLine(
                "<article> bulundu.");

            return article;
        }

        var main =
            document.DocumentNode.SelectSingleNode(
                "//main");

        if (main is not null)
        {
            Console.WriteLine(
                "<main> bulundu.");

            return main;
        }

        var content =
            document.DocumentNode.SelectSingleNode(
                "//*[contains(@class, 'content')]");

        if (content is not null)
        {
            Console.WriteLine(
                "class='content' bulundu.");

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