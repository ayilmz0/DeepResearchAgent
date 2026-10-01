using DeepResearchAgent.Engine.DTOs;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DeepResearchAgent.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResearchController : ControllerBase
{
    private readonly IResearchService _researchService;

    public ResearchController(IResearchService researchService)
    {
        _researchService = researchService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateResearchResponse>> Create(
        [FromBody] CreateResearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _researchService.CreateResearchAsync(
            request,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetResearchResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _researchService.GetResearchByIdAsync(
            id,
            cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}