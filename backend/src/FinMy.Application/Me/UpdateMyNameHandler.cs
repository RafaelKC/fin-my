using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using FinMy.Domain.Common;
using FluentValidation;

namespace FinMy.Application.Profile;

/// <param name="Name">Novo nome da pessoa.</param>
public sealed record UpdateMyNameCommand(string? Name);

/// <summary>Usuário e Espaço depois da alteração, como o cliente os vê (<c>MeResponse</c>).</summary>
public sealed record MeResult(UserSummary User, TenantSummary Tenant);

public sealed class UpdateMyNameValidator : AbstractValidator<UpdateMyNameCommand>
{
    public UpdateMyNameValidator() => RuleFor(c => c.Name).ValidName("Informe seu nome.");
}

/// <summary>Altera o nome do usuário do contexto (US5, FR-013). O Espaço mantém o nome que tem.</summary>
public sealed class UpdateMyNameHandler(IProfileStore profiles, ICurrentContext context, IClock clock)
{
    private static readonly UpdateMyNameValidator Validator = new();

    public async Task<MeResult> HandleAsync(UpdateMyNameCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = context.UserId ?? throw MeErrors.Unauthorized();
        await Validator.ValidateAndThrowAsync(command, cancellationToken);

        return await profiles.RenameUserAsync(userId, command.Name!, clock.UtcNow, cancellationToken)
            ?? throw MeErrors.Unauthorized();
    }
}

internal static class MeErrors
{
    public static DomainException Unauthorized() => new(new DomainError("unauthorized", "Entre novamente para continuar.", DomainErrorKind.Unauthorized));
}
