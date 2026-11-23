using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using FluentValidation;

namespace FinMy.Application.Profile;

/// <param name="Name">Novo nome do Espaço.</param>
public sealed record RenameTenantCommand(string? Name);

public sealed class RenameTenantValidator : AbstractValidator<RenameTenantCommand>
{
    public RenameTenantValidator() => RuleFor(c => c.Name).ValidName("Informe o nome do Espaço.");
}

/// <summary>Renomeia o Espaço do contexto (US5, FR-013); a auditoria registra quem e por qual canal.</summary>
public sealed class RenameTenantHandler(IProfileStore profiles, ICurrentContext context)
{
    private static readonly RenameTenantValidator Validator = new();

    public async Task<TenantSummary> HandleAsync(RenameTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = context.UserId ?? throw MeErrors.Unauthorized();
        var tenantId = context.TenantId ?? throw MeErrors.Unauthorized();
        await Validator.ValidateAndThrowAsync(command, cancellationToken);

        return await profiles.RenameTenantAsync(tenantId, userId, command.Name!, cancellationToken)
            ?? throw MeErrors.Unauthorized();
    }
}
