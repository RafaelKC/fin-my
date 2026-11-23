namespace FinMy.Api.Errors;

/// <summary>Códigos estáveis de erro genéricos (contracts/openapi.yaml, <c>Problem.code</c>). Os de negócio vêm de <c>DomainError</c>.</summary>
public static class ProblemCodes
{
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string ValidationFailed = "validation_failed";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";
    public const string ServiceUnavailable = "service_unavailable";
}
