using FinMy.Domain.Cards;
using FinMy.Domain.Identity;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> builder)
    {
        builder.ToTable("invoice_payments", t =>
        {
            t.HasCheckConstraint("ck_invoice_payments_amount_cents", "amount_cents > 0");
            t.HasCheckConstraint(
                "ck_invoice_payments_payment_date",
                "payment_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.InvoiceId).IsRequired();
        builder.Property(p => p.WalletId).IsRequired();
        builder.Property(p => p.AmountCents).IsRequired();
        builder.Property(p => p.PaymentDate).IsRequired();
        builder.Ignore(p => p.Amount);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Invoice>().WithMany().HasTenantForeignKey(p => p.InvoiceId);
        builder.HasOne<Wallet>().WithMany().HasTenantForeignKey(p => p.WalletId);

        builder.HasIndex(p => new { p.TenantId, p.InvoiceId });
        builder.HasIndex(p => new { p.TenantId, p.WalletId });
    }
}
