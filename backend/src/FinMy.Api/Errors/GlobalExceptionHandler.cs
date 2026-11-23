using System.Text.Json;
using FluentValidation;
using FinMy.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Api.Errors;

/// <summary>Converte exceções em <c>application/problem+json</c> (docs/arquitetura.md §6). Nada interno vaza na resposta.</summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var problem = Map(exception);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        if (problem.Extensions.TryGetValue("retryAfterSeconds", out var retry) && retry is int seconds)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro não tratado em {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);

    internal static ProblemDetails Map(Exception exception) => exception switch
    {
        DomainException domain => ProblemFactory.FromDomainError(domain.Error),
        ValidationException validation => ProblemFactory.Validation(GroupByField(validation)),
        DbUpdateConcurrencyException => ProblemFactory.ForStatus(StatusCodes.Status409Conflict, ProblemCodes.ConcurrencyConflict),
        BadHttpRequestException bad => ProblemFactory.ForStatus(bad.StatusCode),
        JsonException => ProblemFactory.ForStatus(StatusCodes.Status400BadRequest),
        _ => ProblemFactory.ForStatus(StatusCodes.Status500InternalServerError),
    };

    private static Dictionary<string, string[]> GroupByField(ValidationException exception) =>
        exception.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

    private static string ToCamelCase(string name) =>
        string.Join('.', name.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
