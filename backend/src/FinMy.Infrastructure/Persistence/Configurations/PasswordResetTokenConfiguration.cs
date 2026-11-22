using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.TokenHash).HasColumnType("bytea").IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.ExpiresAt).IsRequired();
        builder.Property(p => p.UsedAt);

        builder.HasIndex(p => p.TokenHash).IsUnique();
        builder.HasIndex(p => p.UserId);

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
