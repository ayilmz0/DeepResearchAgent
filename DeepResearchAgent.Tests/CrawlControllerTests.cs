
using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using DeepResearchAgent.WebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace DeepResearchAgent.Tests;

public class CrawlControllerTests
{
    private static CrawlController CreateController(ICrawler crawler)
    {
        return new CrawlController(
            crawler,
            NullLogger<CrawlController>.Instance);
    }

    [Fact]
    public async Task Crawl_EmptyUrl_ReturnsBadRequest()
    {
        var crawler = new FakeCrawler();
        var controller = CreateController(crawler);

        var result = await controller.Crawl(
            " ",
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, crawler.CallCount);
    }

    [Fact]
    public async Task Crawl_ValidUrl_ReturnsOk()
    {
        var expected = new CrawlResultDto
        {
            Title = "Test page",
            Content = "Test content",
            CanonicalUrl = "https://example.com"
        };

        var controller = CreateController(
            new FakeCrawler { Result = expected });

        var result = await controller.Crawl(
            "https://example.com",
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, okResult.Value);
    }

    [Fact]
    public async Task Crawl_InvalidOperation_ReturnsBadRequest()
    {
        var controller = CreateController(
            new FakeCrawler
            {
                Exception = new InvalidOperationException("Blocked URL")
            });

        var result = await controller.Crawl(
            "http://localhost",
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Crawl_HttpRequestException_ReturnsBadGateway()
    {
        var controller = CreateController(
            new FakeCrawler
            {
                Exception = new HttpRequestException("Connection failed")
            });

        var result = await controller.Crawl(
            "https://example.com",
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(
            StatusCodes.Status502BadGateway,
            response.StatusCode);
    }

    [Fact]
    public async Task Crawl_UnexpectedException_ReturnsInternalServerError()
    {
        var controller = CreateController(
            new FakeCrawler
            {
                Exception = new Exception("Unexpected error")
            });

        var result = await controller.Crawl(
            "https://example.com",
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            response.StatusCode);
    }

    private sealed class FakeCrawler : ICrawler
    {
        public CrawlResultDto Result { get; set; } = new();

        public Exception? Exception { get; set; }

        public int CallCount { get; private set; }

        public Task<CrawlResultDto> CrawlAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            cancellationToken.ThrowIfCancellationRequested();

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }
}
