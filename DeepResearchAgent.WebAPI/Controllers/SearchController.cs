using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DeepResearchAgent.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IResearchSearcher _researchSearcher;

    public SearchController(IResearchSearcher researchSearcher)
    {
        _researchSearcher = researchSearcher;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> Search(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Query boş olamaz.");
        }

        var results = await _researchSearcher.SearchAsync(
            query,
            cancellationToken);

        return Ok(results);
    }
}