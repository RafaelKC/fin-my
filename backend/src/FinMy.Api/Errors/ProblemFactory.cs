using FinMy.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinMy.Api.Errors;

/// <summary>Monta os <see cref="ProblemDetails"/> da API: <c>code</c> estável, <c>detail</c> em pt-BR e <c>traceId</c>.</summary>
public static class ProblemFactory
{
    private static readonly Dictionary<int, (string Code, string Title, string Detail)> ByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = (ProblemCodes.BadRequest, "Requisição inválida", "A requisição não pôde ser entendida. Confira os dados e tente novamente."),
        [StatusCodes.Status401Unauthorized] = (ProblemCodes.Unauthorized, "Não autenticado", "Entre na sua conta para continuar."),
        [StatusCodes.Status403Forbidden] = (ProblemCodes.Forbidden, "Acesso negado", "Você não tem permissão para fazer isso."),
        [StatusCodes.Status404NotFound] = (ProblemCodes.NotFound, "Não encontrado", "Não encontramos o que você procura."),
        [StatusCodes.Status405MethodNotAllowed] = (ProblemCodes.MethodNotAllowed, "Método não permitido", "Esta operação não é permitida neste endereço."),
        [StatusCodes.Status409Conflict] = (ProblemCodes.ConcurrencyConflict, "Conflito", "Este registro foi alterado por outra pessoa. Atualize e tente novamente."),
        [StatusCodes.Status415UnsupportedMediaType] = (ProblemCodes.UnsupportedMediaType, "Formato não suportado", "O formato dos dados enviados não é suportado."),
        [StatusCodes.Status422UnprocessableEntity] = (ProblemCodes.ValidationFailed, "Dados inválidos", "Alguns dados estão inválidos. Corrija os campos indicados."),
        [StatusCodes.Status429TooManyRequests] = (ProblemCodes.RateLimited, "Muitas requisições", "Muitas tentativas em pouco tempo. Aguarde um instante e tente de novo."),
        [StatusCodes.Status500InternalServerError] = (ProblemCodes.InternalError, "Erro interno", "Algo deu errado do nosso lado. Tente novamente em instantes."),
        [StatusCodes.Status503ServiceUnavailable] = (ProblemCodes.ServiceUnavailable, "Serviço indisponível", "O serviço está indisponível no momento. Tente novamente em instantes."),
    };

    /// <summary>Problema genérico para um status HTTP (usado também para respostas geradas pelo framework).</summary>
    public static ProblemDetails ForStatus(int status, string? code = null, string? detail = null)
    {
        var known = ByStatus.GetValueOrDefault(status, (ProblemCodes.BadRequest, "Erro", "Não foi possível concluir a operação."));
        return Build(status, code ?? known.Item1, known.Item2, detail ?? known.Item3);
    }

    /// <summary>Problema a partir de um erro de domínio; validação ganha <c>errors</c> e limitação ganha <c>retryAfterSeconds</c>.</summary>
    public static ProblemDetails FromDomainError(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Kind switch
        {
            DomainErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            DomainErrorKind.NotFound => StatusCodes.Status404NotFound,
            DomainErrorKind.Conflict => StatusCodes.Status409Conflict,
            DomainErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            DomainErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            DomainErrorKind.Gone => StatusCodes.Status410Gone,
            DomainErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = Build(status, error.Code, ByStatus.GetValueOrDefault(status).Title ?? "Erro", error.Message);

        if (error.FieldErrors is { Count: > 0 })
        {
            problem.Extensions["errors"] = error.FieldErrors;
        }

        if (error.RetryAfterSeconds is { } retry)
        {
            problem.Extensions["retryAfterSeconds"] = retry;
        }

        if (error.Extensions is { Count: > 0 })
        {
            foreach (var (key, value) in error.Extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }

    /// <summary>422 <c>validation_failed</c> com mensagens por campo (<c>ValidationProblem</c>).</summary>
    public static ProblemDetails Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = ForStatus(StatusCodes.Status422UnprocessableEntity);
        problem.Extensions["errors"] = errors;
        return problem;
    }

    /// <summary>Acrescenta o <c>traceId</c> da requisição quando houver.</summary>
    public static void AddTraceId(ProblemDetails problem, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(context);
        problem.Extensions["traceId"] = context.TraceIdentifier;
    }

    private static ProblemDetails Build(int status, string code, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"urn:finmy:problem:{code}",
            Title = title,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
