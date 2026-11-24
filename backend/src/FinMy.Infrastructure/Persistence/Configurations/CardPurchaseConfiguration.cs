using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class CardPurchaseConfiguration : IEntityTypeConfiguration<CardPurchase>
{
    public void Configure(EntityTypeBuilder<CardPurchase> builder)
    {
        builder.ToTable("card_purchases", t =>
        {
            t.HasCheckConstraint("ck_card_purchases_nature", $"nature IN ({SnakeCaseEnumConverter<PurchaseNature>.SqlList})");
            t.HasCheckConstraint("ck_card_purchases_origin", $"origin IN ({SnakeCaseEnumConverter<PurchaseOrigin>.SqlList})");
            t.HasCheckConstraint("ck_card_purchases_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
            t.HasCheckConstraint(
                "ck_card_purchases_purchase_date",
                "purchase_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");

            // Os três campos de parcela andam juntos, e só a origem "installment" os tem.
            t.HasCheckConstraint(
                "ck_card_purchases_installment_fields",
                "(installment_plan_id IS NULL) = (installment_number IS NULL) "
                + "AND (installment_plan_id IS NULL) = (installment_total IS NULL) "
                + "AND ((origin = 'installment') = (installment_plan_id IS NOT NULL))");
            t.HasCheckConstraint(
                "ck_card_purchases_installment_number",
                "installment_number IS NULL OR (installment_number BETWEEN 1 AND installment_total)");
            t.HasCheckConstraint(
                "ck_card_purchases_installment_total",
                "installment_total IS NULL OR installment_total BETWEEN 1 AND 48");

            // Crédito (estorno) é sempre à vista.
            t.HasCheckConstraint("ck_card_purchases_credit_not_installment", "nature = 'debit' OR installment_plan_id IS NULL");

            // O par de "saldo levado" existe se, e somente se, a origem é carry_over.
            t.HasCheckConstraint(
                "ck_card_purchases_carry_over_pair",
                "(origin = 'carry_over') = (carry_over_pair_id IS NOT NULL)");

            // A origem "recurrence" existe se, e somente se, há recorrência (005).
            t.HasCheckConstraint(
                "ck_card_purchases_recurrence",
                "(recurrence_id IS NULL) = (recurrence_month IS NULL) "
                + "AND (recurrence_month IS NULL OR extract(day from recurrence_month) = 1) "
                + "AND ((origin = 'recurrence') = (recurrence_id IS NOT NULL))");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.CreditCardId).IsRequired();
        builder.Property(p => p.InvoiceId).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(80).IsRequired();
        builder.Property(p => p.DescriptionNormalized).HasMaxLength(80).IsRequired();
        builder.Property(p => p.AmountCents).IsRequired();
        builder.Property(p => p.Nature)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<PurchaseNature>())
            .IsRequired();
        builder.Property(p => p.PurchaseDate).IsRequired();
        builder.Property(p => p.Origin)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<PurchaseOrigin>())
            .IsRequired();
        builder.Property(p => p.Reconciled).IsRequired();
        builder.Property(p => p.ManuallyMoved).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.Property(p => p.RecurrenceModified).HasDefaultValue(false).IsRequired();
        builder.Property(p => p.InstallmentNumber).HasColumnType("smallint");
        builder.Property(p => p.InstallmentTotal).HasColumnType("smallint");
        builder.Ignore(p => p.Amount);
        builder.Ignore(p => p.DisplayDescription);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CreditCard>().WithMany().HasTenantForeignKey(p => p.CreditCardId);

        // FK composta opcional (parcelas); HasTenantForeignKey só aceita chave não nula.
        builder.HasOne<InstallmentPlan>().WithMany()
            .HasForeignKey(nameof(CardPurchase.TenantId), nameof(CardPurchase.InstallmentPlanId))
            .HasPrincipalKey(nameof(InstallmentPlan.TenantId), nameof(InstallmentPlan.Id))
            .OnDelete(DeleteBehavior.Restrict);

        // FK tripla: a fatura tem de ser do mesmo cartão da compra (e do mesmo Espaço).
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(nameof(CardPurchase.TenantId), nameof(CardPurchase.InvoiceId), nameof(CardPurchase.CreditCardId))
            .HasPrincipalKey(nameof(Invoice.TenantId), nameof(Invoice.Id), nameof(Invoice.CreditCardId))
            .OnDelete(DeleteBehavior.Restrict);

        // FK composta opcional (Tag de acerto).
        builder.HasOne<Tag>().WithMany()
            .HasForeignKey(nameof(CardPurchase.TenantId), nameof(CardPurchase.SettlementTagId))
            .HasPrincipalKey(nameof(Tag.TenantId), nameof(Tag.Id))
            .OnDelete(DeleteBehavior.Restrict);

        // FK composta opcional (recorrência de origem).
        builder.HasOne<Recurrence>().WithMany()
            .HasForeignKey(nameof(CardPurchase.TenantId), nameof(CardPurchase.RecurrenceId))
            .HasPrincipalKey(nameof(Recurrence.TenantId), nameof(Recurrence.Id))
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.TenantId, p.RecurrenceId, p.RecurrenceMonth })
            .IsUnique()
            .HasFilter("deleted_at IS NULL AND recurrence_id IS NOT NULL");
        builder.HasIndex(p => new { p.TenantId, p.SettlementTagId });
        builder.HasIndex(p => new { p.TenantId, p.SplitGroupId }).HasFilter("split_group_id IS NOT NULL");
        builder.HasIndex(p => new { p.TenantId, p.InvoiceId });
        builder.HasIndex(p => new { p.TenantId, p.InstallmentPlanId });
        builder.HasIndex(p => new { p.TenantId, p.DescriptionNormalized });
        builder.HasIndex(p => new { p.TenantId, p.CreditCardId });
        builder.HasIndex(p => new { p.TenantId, p.CarryOverPairId }).HasFilter("carry_over_pair_id IS NOT NULL");
    }
}
