using FinMy.Domain.Identity;
using FinMy.Domain.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence.Configurations;

internal sealed class SuggestionConfiguration : IEntityTypeConfiguration<Suggestion>
{
    public void Configure(EntityTypeBuilder<Suggestion> builder)
    {
        builder.ToTable("suggestions", t => t.HasCheckConstraint("ck_suggestions_amount", "amount_cents > 0"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Source).HasMaxLength(Suggestion.SourceMaxLength).IsRequired();
        builder.Property(s => s.Bank).HasMaxLength(3);
        builder.Property(s => s.Kind).HasMaxLength(16).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(Suggestion.DescriptionMaxLength).IsRequired();
        builder.Property(s => s.RawText).HasMaxLength(Suggestion.RawTextMaxLength).IsRequired();
        builder.Property(s => s.DedupeKey).HasMaxLength(Suggestion.DedupeKeyMaxLength).IsRequired();
        builder.Property(s => s.CardLast4).HasMaxLength(4);
        builder.Property(s => s.Status).HasConversion(new SnakeCaseEnumConverter<SuggestionStatus>()).HasMaxLength(16).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        // A mesma notificação (chave do celular) só vira uma sugestão por Espaço.
        builder.HasIndex(s => new { s.TenantId, s.DedupeKey }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.Status });
    }
}
