using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class CreditCardConfiguration : IEntityTypeConfiguration<CreditCard>
{
    public void Configure(EntityTypeBuilder<CreditCard> builder)
    {
        builder.ToTable("credit_cards", t =>
        {
            t.HasCheckConstraint("ck_credit_cards_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
            t.HasCheckConstraint("ck_credit_cards_closing_day", "closing_day BETWEEN 1 AND 31");
            t.HasCheckConstraint("ck_credit_cards_due_day", "due_day BETWEEN 1 AND 31");
            t.HasCheckConstraint("ck_credit_cards_limit_cents", "limit_cents IS NULL OR (limit_cents >= 0 AND limit_cents <= 999999999)");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(CreditCard.NameMaxLength).IsRequired();
        builder.Property(c => c.NameNormalized).HasMaxLength(CreditCard.NameMaxLength).IsRequired();
        builder.Property(c => c.Color).HasMaxLength(7).IsRequired();
        builder.Property(c => c.ClosingDay).HasColumnType("smallint").IsRequired();
        builder.Property(c => c.DueDay).HasColumnType("smallint").IsRequired();
        builder.Property(c => c.PaymentWalletId).IsRequired();
        builder.Property(c => c.LimitCents);
        builder.Property(c => c.ArchivedAt);
        builder.Ignore(c => c.IsArchived);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Wallet>().WithMany().HasTenantForeignKey(c => c.PaymentWalletId);

        // Nome único no Espaço (sem acento/caixa) entre os não excluídos; arquivados continuam ocupando o nome.
        builder.HasIndex(c => new { c.TenantId, c.NameNormalized })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(c => new { c.TenantId, c.PaymentWalletId });
    }
}
