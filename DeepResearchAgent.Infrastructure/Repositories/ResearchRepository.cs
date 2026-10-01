using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DeepResearchAgent.Infrastructure.Persistence.Repositories;

public class ResearchRepository : IResearchRepository
{
    private readonly AppDbContext _context;

    public ResearchRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        Research research,
        CancellationToken cancellationToken = default)
    {
        await _context.Researches.AddAsync(
            research,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Research?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.Researches
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }
}