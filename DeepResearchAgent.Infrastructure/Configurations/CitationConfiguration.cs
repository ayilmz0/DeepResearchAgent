using DeepResearchAgent.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepResearchAgent.Infrastructure.Persistence.Configurations;

public class CitationConfiguration : IEntityTypeConfiguration<Citation>
{
    public void Configure(EntityTypeBuilder<Citation> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Fact)
            .WithMany(x => x.Citations)
            .HasForeignKey(x => x.FactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Source)
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}