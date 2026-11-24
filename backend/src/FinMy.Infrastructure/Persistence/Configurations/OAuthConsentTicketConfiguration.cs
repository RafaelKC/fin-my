using FinMy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class OAuthConsentTicketConfiguration : IEntityTypeConfiguration<OAuthConsentTicket>
{
    public void Configure(EntityTypeBuilder<OAuthConsentTicket> builder)
    {
        builder.ToTable("oauth_consent_tickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TicketHash).HasColumnType("bytea").IsRequired();
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.ClientId).IsRequired();
        builder.Property(t => t.RedirectUri).IsRequired();
        builder.Property(t => t.CodeChallenge).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.UsedAt);

        builder.HasIndex(t => t.TicketHash).IsUnique();
        builder.HasIndex(t => t.ExpiresAt);

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
