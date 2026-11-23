using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class InstallmentPlanConfiguration : IEntityTypeConfiguration<InstallmentPlan>
{
    public void Configure(EntityTypeBuilder<InstallmentPlan> builder)
    {
        builder.ToTable("installment_plans", t =>
        {
            t.HasCheckConstraint("ck_installment_plans_mode", $"mode IN ({SnakeCaseEnumConverter<InstallmentMode>.SqlList})");
            t.HasCheckConstraint("ck_installment_plans_installment_total", "installment_total BETWEEN 1 AND 48");
            t.HasCheckConstraint("ck_installment_plans_first_number", "first_number BETWEEN 1 AND installment_total");
            t.HasCheckConstraint(
                "ck_installment_plans_first_number_mode",
                "mode = 'in_progress' OR first_number = 1");
            t.HasCheckConstraint(
                "ck_installment_plans_amount_by_mode",
                "(mode = 'total_amount' AND total_amount_cents IS NOT NULL AND installment_amount_cents IS NULL) "
                + "OR (mode <> 'total_amount' AND installment_amount_cents IS NOT NULL AND total_amount_cents IS NULL)");
            t.HasCheckConstraint(
                "ck_installment_plans_total_amount_cents",
                "total_amount_cents IS NULL OR (total_amount_cents > 0 AND total_amount_cents <= 999999999)");
            t.HasCheckConstraint(
                "ck_installment_plans_installment_amount_cents",
                "installment_amount_cents IS NULL OR (installment_amount_cents > 0 AND installment_amount_cents <= 999999999)");
            t.HasCheckConstraint(
                "ck_installment_plans_purchase_date",
                "purchase_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
            t.HasCheckConstraint(
                "ck_installment_plans_first_invoice_due_month",
                "extract(day from first_invoice_due_month) = 1 AND first_invoice_due_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.CreditCardId).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Mode)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<InstallmentMode>())
            .IsRequired();
        builder.Property(p => p.InstallmentTotal).HasColumnType("smallint").IsRequired();
        builder.Property(p => p.FirstNumber).HasColumnType("smallint").IsRequired();
        builder.Property(p => p.TotalAmountCents);
        builder.Property(p => p.InstallmentAmountCents);
        builder.Property(p => p.PurchaseDate).IsRequired();
        builder.Property(p => p.FirstInvoiceDueMonth).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CreditCard>().WithMany().HasTenantForeignKey(p => p.CreditCardId);

        builder.HasIndex(p => new { p.TenantId, p.CreditCardId });
    }
}
