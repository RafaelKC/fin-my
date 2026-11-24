using FinMy.Domain.Identity;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class RecurrenceTagConfiguration : IEntityTypeConfiguration<RecurrenceTag>
{
    public void Configure(EntityTypeBuilder<RecurrenceTag> builder)
    {
        builder.ToTable("recurrence_tags");

        builder.HasKey(l => new { l.RecurrenceId, l.TagId });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Recurrence>().WithMany().HasTenantForeignKey(l => l.RecurrenceId);
        builder.HasOne<Tag>().WithMany().HasTenantForeignKey(l => l.TagId);

        builder.HasIndex(l => new { l.TenantId, l.TagId });
    }
}
