using FinMy.Api.Composition;
using FinMy.Api.Endpoints;
using FinMy.Domain.Common;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace FinMy.Api.Tests.Support;

/// <summary>Endpoints só de teste (registrados via <see cref="IEndpointModule"/>) para exercitar erros e limites.</summary>
public sealed class TestEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/test/not-found", () => { throw new DomainException(new DomainError("wallet.not_found", "Carteira não encontrada.", DomainErrorKind.NotFound)); });
        app.MapGet("/test/conflict", () => { throw new DomainException(new DomainError("email_already_registered", "Este e-mail já tem conta. Entrar ou recuperar senha?", DomainErrorKind.Conflict)); });
        app.MapGet("/test/locked", () => { throw new DomainException(new DomainError("login_locked", "Muitas tentativas. Tente de novo em 15 minutos.", DomainErrorKind.TooManyRequests, RetryAfterSeconds: 900)); });
        app.MapGet("/test/domain-validation", () => { throw new DomainException(new DomainError("validation_failed", "Dados inválidos.", DomainErrorKind.Validation, new Dictionary<string, string[]> { ["name"] = ["Informe o nome."] })); });
        app.MapGet("/test/validation", () => { throw new ValidationException([new ValidationFailure("Name", "Informe o nome."), new ValidationFailure("Name", "O nome é muito longo."), new ValidationFailure("Address.ZipCode", "CEP inválido.")]); });
        app.MapGet("/test/boom", (string? email) => { throw new InvalidOperationException($"segredo interno de {email}"); });
        app.MapGet("/test/domain-result", () => ProblemResultsAdapter.NotFound());
        app.MapPost("/test/echo", (EchoBody body) => Results.Ok(body));
        app.MapGet("/test/limited", () => "ok").RequireRateLimiting(RateLimitPolicies.Auth);
    }
}

public sealed record EchoBody(string Name);

internal static class ProblemResultsAdapter
{
    public static IResult NotFound() => FinMy.Api.Errors.ProblemResults.From(new DomainError("not_found", "Não encontramos o que você procura.", DomainErrorKind.NotFound));
}
