using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class TenantMemberConfiguration : IEntityTypeConfiguration<TenantMember>
{
    public void Configure(EntityTypeBuilder<TenantMember> builder)
    {
        builder.ToTable("tenant_members", t =>
            t.HasCheckConstraint("ck_tenant_members_role", $"role IN ({SnakeCaseEnumConverter<TenantMemberRole>.SqlList})"));

        builder.HasKey(m => new { m.TenantId, m.UserId });

        builder.Property(m => m.Role)
            .HasColumnType("text")
            .HasConversion(new SnakeCaseEnumConverter<TenantMemberRole>())
            .IsRequired();
        builder.Property(m => m.JoinedAt).IsRequired();

        // v1: um Espaço por usuário e um participante por Espaço (removíveis quando houver compartilhamento).
        builder.HasIndex(m => m.UserId).IsUnique();
        builder.HasIndex(m => m.TenantId).IsUnique();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
