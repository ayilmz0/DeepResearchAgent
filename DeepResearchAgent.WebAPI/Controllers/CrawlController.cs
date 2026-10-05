using DeepResearchAgent.Engine.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DeepResearchAgent.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CrawlController : ControllerBase
{
    private readonly ICrawler _crawler;

    public CrawlController(ICrawler crawler)
    {
        _crawler = crawler;
    }

    [HttpGet]
    public async Task<ActionResult<string>> Crawl(
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest("URL boş olamaz.");
        }

        var content = await _crawler.CrawlAsync(
            url,
            cancellationToken);

        return Ok(content);
    }
}