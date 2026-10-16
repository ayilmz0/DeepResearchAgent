using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DeepResearchAgent.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CrawlController : ControllerBase
{
    private readonly ICrawler _crawler;
    private readonly ILogger<CrawlController> _logger;

    public CrawlController(
        ICrawler crawler,
        ILogger<CrawlController> logger)
    {
        _crawler = crawler;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<CrawlResultDto>> Crawl(
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest("URL boş olamaz.");
        }

        try
        {
            var result = await _crawler.CrawlAsync(
                url,
                cancellationToken);

            return Ok(result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Crawler URL doğrulaması başarısız.");

            return BadRequest(
                "URL geçersiz veya erişim güvenlik politikası tarafından engellendi.");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Crawler isteği zaman aşımına uğradı.");

            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                "Kaynak taranırken zaman aşımı oluştu.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Crawler uzak kaynağa erişemedi.");

            return StatusCode(
                StatusCodes.Status502BadGateway,
                "Uzak kaynağa erişilemedi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Crawler endpoint'inde beklenmeyen hata.");

            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Kaynak taranırken beklenmeyen bir hata oluştu.");
        }
    }
}