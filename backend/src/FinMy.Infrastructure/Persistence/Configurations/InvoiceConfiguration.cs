using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint(
                "ck_invoices_due_month",
                "extract(day from due_month) = 1 AND due_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
            t.HasCheckConstraint("ck_invoices_closing_date", "closing_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
            t.HasCheckConstraint("ck_invoices_due_date", "due_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.CreditCardId).IsRequired();
        builder.Property(i => i.DueMonth).IsRequired();
        builder.Property(i => i.ClosingDate).IsRequired();
        builder.Property(i => i.DueDate).IsRequired();
        builder.Ignore(i => i.DueYearMonth);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CreditCard>().WithMany().HasTenantForeignKey(i => i.CreditCardId);

        // Alvo da FK tripla das compras (compra e fatura sempre do mesmo cartão).
        builder.HasAlternateKey(i => new { i.TenantId, i.Id, i.CreditCardId })
            .HasName("ak_invoices_tenant_id_id_credit_card_id");

        // Uma fatura por cartão e mês; o ON CONFLICT do GetOrCreate (research R2) usa este índice.
        builder.HasIndex(i => new { i.TenantId, i.CreditCardId, i.DueMonth }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.DueMonth });
    }
}
