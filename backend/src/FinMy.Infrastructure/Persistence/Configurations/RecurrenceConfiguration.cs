using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Tags;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class RecurrenceConfiguration : IEntityTypeConfiguration<Recurrence>
{
    public void Configure(EntityTypeBuilder<Recurrence> builder)
    {
        builder.ToTable("recurrences", t =>
        {
            t.HasCheckConstraint("ck_recurrences_kind", $"kind IN ({SnakeCaseEnumConverter<RecurrenceKind>.SqlList})");
            t.HasCheckConstraint("ck_recurrences_end_type", $"end_type IN ({SnakeCaseEnumConverter<RecurrenceEndType>.SqlList})");
            t.HasCheckConstraint(
                "ck_recurrences_reference_month_rule",
                $"reference_month_rule IS NULL OR reference_month_rule IN ({SnakeCaseEnumConverter<ReferenceMonthRule>.SqlList})");
            t.HasCheckConstraint("ck_recurrences_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
            t.HasCheckConstraint("ck_recurrences_day_of_month", "day_of_month BETWEEN 1 AND 31");
            t.HasCheckConstraint(
                "ck_recurrences_start_month",
                "extract(day from start_month) = 1 AND start_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
            t.HasCheckConstraint(
                "ck_recurrences_end_month",
                "end_month IS NULL OR (extract(day from end_month) = 1 AND end_month >= start_month AND end_month <= DATE '2100-12-31')");
            t.HasCheckConstraint(
                "ck_recurrences_generated_until_month",
                "generated_until_month IS NULL OR extract(day from generated_until_month) = 1");
            t.HasCheckConstraint("ck_recurrences_end_month_by_type", "(end_type = 'none') = (end_month IS NULL)");
            t.HasCheckConstraint(
                "ck_recurrences_occurrence_limit",
                "(end_type = 'after_count' AND occurrence_limit BETWEEN 1 AND 360) OR (end_type <> 'after_count' AND occurrence_limit IS NULL)");

            // Carteira (entrada/saída) e cartão (compra) são exclusivos, e a regra de mês só existe para entrada/saída.
            t.HasCheckConstraint(
                "ck_recurrences_target",
                "(kind = 'card_purchase' AND credit_card_id IS NOT NULL AND wallet_id IS NULL AND reference_month_rule IS NULL) "
                + "OR (kind <> 'card_purchase' AND wallet_id IS NOT NULL AND credit_card_id IS NULL AND reference_month_rule IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Kind)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<RecurrenceKind>())
            .IsRequired();
        builder.Property(r => r.Description).HasMaxLength(80).IsRequired();
        builder.Property(r => r.DescriptionNormalized).HasMaxLength(80).IsRequired();
        builder.Property(r => r.AmountCents).IsRequired();
        builder.Property(r => r.DayOfMonth).HasColumnType("smallint").IsRequired();
        builder.Property(r => r.StartMonth).IsRequired();
        builder.Property(r => r.EndType)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<RecurrenceEndType>())
            .IsRequired();
        builder.Property(r => r.OccurrenceLimit).HasColumnType("smallint");
        builder.Property(r => r.ReferenceMonthRule)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<ReferenceMonthRule>());
        builder.Ignore(r => r.Amount);
        builder.Ignore(r => r.StartYearMonth);
        builder.Ignore(r => r.EndYearMonth);
        builder.Ignore(r => r.GeneratedUntil);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);

        // FKs compostas opcionais; HasTenantForeignKey só aceita chave não nula.
        builder.HasOne<Wallet>().WithMany()
            .HasForeignKey(nameof(Recurrence.TenantId), nameof(Recurrence.WalletId))
            .HasPrincipalKey(nameof(Wallet.TenantId), nameof(Wallet.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CreditCard>().WithMany()
            .HasForeignKey(nameof(Recurrence.TenantId), nameof(Recurrence.CreditCardId))
            .HasPrincipalKey(nameof(CreditCard.TenantId), nameof(CreditCard.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tag>().WithMany()
            .HasForeignKey(nameof(Recurrence.TenantId), nameof(Recurrence.SettlementTagId))
            .HasPrincipalKey(nameof(Tag.TenantId), nameof(Tag.Id))
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.TenantId, r.WalletId });
        builder.HasIndex(r => new { r.TenantId, r.CreditCardId });
        builder.HasIndex(r => new { r.TenantId, r.SettlementTagId });
    }
}
