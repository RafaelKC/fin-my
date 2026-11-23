using FinMy.Domain.Identity;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets", t =>
        {
            t.HasCheckConstraint("ck_wallets_type", $"type IN ({SnakeCaseEnumConverter<WalletType>.SqlList})");
            t.HasCheckConstraint("ck_wallets_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
            t.HasCheckConstraint("ck_wallets_initial_balance_cents", "abs(initial_balance_cents) <= 999999999");
            t.HasCheckConstraint(
                "ck_wallets_initial_balance_date",
                "initial_balance_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
            t.HasCheckConstraint("ck_wallets_primary_not_archived", "NOT (is_primary AND archived_at IS NOT NULL)");
        });

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).HasMaxLength(Wallet.NameMaxLength).IsRequired();
        builder.Property(w => w.NameNormalized).HasMaxLength(Wallet.NameMaxLength).IsRequired();
        builder.Property(w => w.Type)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<WalletType>())
            .IsRequired();
        builder.Property(w => w.Color).HasMaxLength(7).IsRequired();
        builder.Property(w => w.InitialBalanceCents).IsRequired();
        builder.Property(w => w.InitialBalanceDate).IsRequired();
        builder.Property(w => w.IsPrimary).IsRequired();
        builder.Property(w => w.ArchivedAt);
        builder.Ignore(w => w.IsArchived);
        builder.Ignore(w => w.InitialBalance);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Nome único no Espaço (sem acento/caixa) entre as não excluídas; arquivadas continuam ocupando o nome.
        builder.HasIndex(w => new { w.TenantId, w.NameNormalized })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // No máximo uma principal ativa por Espaço (research R6).
        builder.HasIndex(w => w.TenantId)
            .HasDatabaseName("ux_wallets_tenant_id_primary")
            .IsUnique()
            .HasFilter("is_primary AND archived_at IS NULL AND deleted_at IS NULL");
    }
}
