using FinMy.Domain.Entries;
using FinMy.Domain.Identity;
using FinMy.Domain.Tags;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries", t =>
        {
            t.HasCheckConstraint("ck_entries_kind", $"kind IN ({SnakeCaseEnumConverter<EntryKind>.SqlList})");
            t.HasCheckConstraint("ck_entries_status", $"status IN ({SnakeCaseEnumConverter<EntryStatus>.SqlList})");
            t.HasCheckConstraint("ck_entries_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
            t.HasCheckConstraint("ck_entries_entry_date", "entry_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
            t.HasCheckConstraint(
                "ck_entries_reference_month",
                "extract(day from reference_month) = 1 AND reference_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<EntryKind>())
            .IsRequired();
        builder.Property(e => e.Description).HasMaxLength(80).IsRequired();
        builder.Property(e => e.DescriptionNormalized).HasMaxLength(80).IsRequired();
        builder.Property(e => e.AmountCents).IsRequired();
        builder.Property(e => e.EntryDate).IsRequired();
        builder.Property(e => e.ReferenceMonth).IsRequired();
        builder.Property(e => e.Status)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<EntryStatus>())
            .IsRequired();
        builder.Property(e => e.WalletId).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Ignore(e => e.Amount);
        builder.Ignore(e => e.ReferenceYearMonth);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Wallet>().WithMany().HasTenantForeignKey(e => e.WalletId);

        // FK composta opcional (Tag de acerto); HasTenantForeignKey só aceita chave não nula.
        builder.HasOne<Tag>().WithMany()
            .HasForeignKey(nameof(Entry.TenantId), nameof(Entry.SettlementTagId))
            .HasPrincipalKey(nameof(Tag.TenantId), nameof(Tag.Id))
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.SettlementTagId });
        builder.HasIndex(e => new { e.TenantId, e.SplitGroupId }).HasFilter("split_group_id IS NOT NULL");
        builder.HasIndex(e => new { e.TenantId, e.DescriptionNormalized });
        builder.HasIndex(e => new { e.TenantId, e.ReferenceMonth });
        builder.HasIndex(e => new { e.TenantId, e.Status, e.ReferenceMonth });
        builder.HasIndex(e => new { e.TenantId, e.WalletId });
    }
}
