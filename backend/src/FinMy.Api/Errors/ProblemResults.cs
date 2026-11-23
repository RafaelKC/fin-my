using FinMy.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace FinMy.Api.Errors;

/// <summary>Resultados HTTP de erro para os endpoints (sem lançar exceção no fluxo esperado).</summary>
public static class ProblemResults
{
    /// <summary>Converte um <see cref="DomainError"/> em <c>application/problem+json</c>.</summary>
    public static IResult From(DomainError error)
    {
        var problem = ProblemFactory.FromDomainError(error);
        return new ProblemResult(problem);
    }

    private sealed class ProblemResult(Microsoft.AspNetCore.Mvc.ProblemDetails problem) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ProblemFactory.AddTraceId(problem, httpContext);
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            if (problem.Extensions.TryGetValue("retryAfterSeconds", out var retry) && retry is int seconds)
            {
                httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", httpContext.RequestAborted);
        }
    }
}
