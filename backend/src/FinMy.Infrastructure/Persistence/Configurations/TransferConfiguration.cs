using FinMy.Domain.Identity;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("transfers", t =>
        {
            t.HasCheckConstraint("ck_transfers_different_wallets", "from_wallet_id <> to_wallet_id");
            t.HasCheckConstraint("ck_transfers_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
            t.HasCheckConstraint(
                "ck_transfers_transfer_date",
                "transfer_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.FromWalletId).IsRequired();
        builder.Property(t => t.ToWalletId).IsRequired();
        builder.Property(t => t.AmountCents).IsRequired();
        builder.Property(t => t.TransferDate).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(80).IsRequired();
        builder.Property(t => t.DescriptionNormalized).HasMaxLength(80).IsRequired();
        builder.Property(t => t.Notes).HasMaxLength(500);
        builder.Ignore(t => t.Amount);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Wallet>().WithMany().HasTenantForeignKey(t => t.FromWalletId);
        builder.HasOne<Wallet>().WithMany().HasTenantForeignKey(t => t.ToWalletId);

        builder.HasIndex(t => new { t.TenantId, t.TransferDate });
        builder.HasIndex(t => new { t.TenantId, t.FromWalletId });
        builder.HasIndex(t => new { t.TenantId, t.ToWalletId });
    }
}
