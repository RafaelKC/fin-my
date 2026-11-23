using FinMy.Domain.Entries;
using FinMy.Domain.Identity;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class EntryTagConfiguration : IEntityTypeConfiguration<EntryTag>
{
    public void Configure(EntityTypeBuilder<EntryTag> builder)
    {
        builder.ToTable("entry_tags");

        builder.HasKey(l => new { l.EntryId, l.TagId });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);

        // FKs compostas para os dois lados: o banco recusa ligar lançamento e tag de Espaços diferentes.
        builder.HasOne<Entry>().WithMany().HasTenantForeignKey(l => l.EntryId);
        builder.HasOne<Tag>().WithMany().HasTenantForeignKey(l => l.TagId);

        builder.HasIndex(l => new { l.TenantId, l.TagId });
    }
}
