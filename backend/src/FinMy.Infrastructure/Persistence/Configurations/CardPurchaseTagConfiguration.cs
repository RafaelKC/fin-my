using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class CardPurchaseTagConfiguration : IEntityTypeConfiguration<CardPurchaseTag>
{
    public void Configure(EntityTypeBuilder<CardPurchaseTag> builder)
    {
        builder.ToTable("card_purchase_tags");

        builder.HasKey(l => new { l.CardPurchaseId, l.TagId });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CardPurchase>().WithMany().HasTenantForeignKey(l => l.CardPurchaseId);
        builder.HasOne<Tag>().WithMany().HasTenantForeignKey(l => l.TagId);

        builder.HasIndex(l => new { l.TenantId, l.TagId });
    }
}
