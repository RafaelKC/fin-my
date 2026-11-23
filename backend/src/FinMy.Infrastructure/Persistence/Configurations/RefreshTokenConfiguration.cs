using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", t =>
            t.HasCheckConstraint(
                "ck_refresh_tokens_revoked_reason",
                $"revoked_reason IS NULL OR revoked_reason IN ({SnakeCaseEnumConverter<RefreshTokenRevocationReason>.SqlList})"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.FamilyId).IsRequired();
        builder.Property(r => r.TokenHash).HasColumnType("bytea").IsRequired();
        builder.Property(r => r.DeviceLabel).HasMaxLength(120);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.LastUsedAt).IsRequired();
        builder.Property(r => r.ExpiresAt).IsRequired();
        builder.Property(r => r.RevokedAt);
        builder.Property(r => r.RevokedReason)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<RefreshTokenRevocationReason>());
        builder.Property(r => r.ReplacedById);

        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.HasIndex(r => r.UserId);

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
