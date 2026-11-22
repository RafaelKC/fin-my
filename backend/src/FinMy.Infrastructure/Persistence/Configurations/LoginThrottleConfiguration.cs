using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class LoginThrottleConfiguration : IEntityTypeConfiguration<LoginThrottle>
{
    public void Configure(EntityTypeBuilder<LoginThrottle> builder)
    {
        builder.ToTable("login_throttles");
        builder.HasKey(l => l.EmailNormalized);

        builder.Property(l => l.EmailNormalized).HasMaxLength(254).ValueGeneratedNever();
        builder.Property(l => l.FailedCount).IsRequired();
        builder.Property(l => l.WindowStartedAt).IsRequired();
        builder.Property(l => l.LockedUntil);
    }
}
