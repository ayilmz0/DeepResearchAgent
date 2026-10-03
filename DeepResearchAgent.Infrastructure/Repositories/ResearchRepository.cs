using DeepResearchAgent.Core.Entities;
using DeepResearchAgent.Core.Enums;
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

    public async Task<Research?> GetPendingResearchAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Researches
            .Where(x => x.Status == ResearchStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Research research,
        CancellationToken cancellationToken = default)
    {
        _context.Researches.Update(research);

        await _context.SaveChangesAsync(cancellationToken);
    }
}