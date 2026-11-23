using FinMy.Domain.Identity;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", t => t.HasCheckConstraint("ck_tags_color", "color ~ '^#[0-9A-Fa-f]{6}$'"));

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(Tag.NameMaxLength).IsRequired();
        builder.Property(t => t.NameNormalized).HasMaxLength(Tag.NameMaxLength).IsRequired();
        builder.Property(t => t.Color).HasMaxLength(7).IsRequired();
        builder.Property(t => t.IsSettlement).HasDefaultValue(false).IsRequired();
        builder.Property(t => t.ArchivedAt);
        builder.Ignore(t => t.IsArchived);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Nome único no Espaço (sem acento/caixa) entre as não excluídas; arquivadas continuam ocupando o nome.
        builder.HasIndex(t => new { t.TenantId, t.NameNormalized })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
