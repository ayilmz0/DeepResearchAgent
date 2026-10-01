using DeepResearchAgent.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepResearchAgent.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Research> Researches => Set<Research>();

    public DbSet<ResearchTask> ResearchTasks => Set<ResearchTask>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Fact> Facts => Set<Fact>();

    public DbSet<Citation> Citations => Set<Citation>();

    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

