using FinMy.Domain.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(k => new { k.TenantId, k.Key });

        builder.Property(k => k.TenantId).ValueGeneratedNever();
        builder.Property(k => k.Key).ValueGeneratedNever();
        builder.Property(k => k.UserId);
        builder.Property(k => k.RequestHash).IsRequired();
        builder.Property(k => k.StatusCode).IsRequired();
        builder.Property(k => k.ResponseBody).HasColumnType("jsonb");
        builder.Property(k => k.CreatedAt).IsRequired();

        // Apoia o expurgo diário (created_at > 24 h).
        builder.HasIndex(k => k.CreatedAt);
    }
}
