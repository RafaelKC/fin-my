using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class OAuthAuthorizationUsageConfiguration : IEntityTypeConfiguration<OAuthAuthorizationUsage>
{
    public void Configure(EntityTypeBuilder<OAuthAuthorizationUsage> builder)
    {
        builder.ToTable("oauth_authorization_usage");
        builder.HasKey(u => u.AuthorizationId);
        builder.Property(u => u.AuthorizationId).ValueGeneratedNever();
        builder.Property(u => u.UserId).IsRequired();
        builder.Property(u => u.LastUsedAt).IsRequired();

        builder.HasIndex(u => u.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
