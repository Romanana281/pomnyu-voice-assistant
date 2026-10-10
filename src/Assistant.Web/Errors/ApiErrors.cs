using System.Diagnostics;
using Assistant.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;

namespace Assistant.Web.Errors;

public sealed record ApiError(string Code, string Message, string? Field = null, string? RequestId = null);

public static class ApiErrors
{
    public static IResult Invalid(string field, string message) =>
        Results.BadRequest(new ApiError("invalid_input", message, field));

    public static IResult NotFound(string message) =>
        Results.NotFound(new ApiError("not_found", message));
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var requestId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        var (status, error) = exception switch
        {
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                new ApiError("invalid_input", "Некорректный запрос: проверьте формат данных.", RequestId: requestId)),
            LlmUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                new ApiError("llm_unavailable", "Не получается связаться с языковой моделью. Попробуйте ещё раз через минуту.", RequestId: requestId)),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (
                StatusCodes.Status499ClientClosedRequest,
                new ApiError("cancelled", "Запрос отменён.", RequestId: requestId)),
            _ => (
                StatusCodes.Status500InternalServerError,
                new ApiError("internal", "Что-то пошло не так. Мы уже знаем об ошибке.", RequestId: requestId)),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Необработанная ошибка {RequestId} на {Path}", requestId, http.Request.Path);
        else if (status == StatusCodes.Status503ServiceUnavailable)
            logger.LogWarning("Языковая модель недоступна {RequestId}: {Reason}", requestId, exception.Message);

        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(error, cancellationToken);
        return true;
    }
}
