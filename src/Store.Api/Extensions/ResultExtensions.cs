using Store.Application.Common;

namespace Store.Api.Extensions;

/// <summary>
/// Maps an application <see cref="Result"/> onto an HTTP response.
/// </summary>
/// <remarks>
/// Centralising this is what keeps status codes consistent across ~150 endpoints. An endpoint
/// returns its domain outcome and never picks a status code by hand, so "not found" cannot be a
/// 400 in one place and a 404 in another. Failures are emitted as RFC 9457 ProblemDetails.
/// </remarks>
public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result, int successStatusCode = StatusCodes.Status204NoContent) =>
        result.Succeeded
            ? successStatusCode == StatusCodes.Status204NoContent
                ? Results.NoContent()
                : Results.StatusCode(successStatusCode)
            : Problem(result.Kind, result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.Succeeded
            ? Results.Ok(result.Value)
            : Problem(result.Kind, result.Error);

    /// <summary>For POSTs that create a resource, so the response carries a Location header.</summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> locationFactory) =>
        result.Succeeded
            ? Results.Created(locationFactory(result.Value!), result.Value)
            : Problem(result.Kind, result.Error);

    private static IResult Problem(ErrorKind kind, string? error) => kind switch
    {
        ErrorKind.NotFound => Results.Problem(
            title: "Not found",
            detail: error,
            statusCode: StatusCodes.Status404NotFound),

        ErrorKind.Forbidden => Results.Problem(
            title: "Forbidden",
            detail: error,
            statusCode: StatusCodes.Status403Forbidden),

        ErrorKind.Unauthorized => Results.Problem(
            title: "Unauthorized",
            detail: error,
            statusCode: StatusCodes.Status401Unauthorized),

        ErrorKind.Conflict => Results.Problem(
            title: "Conflict",
            detail: error,
            statusCode: StatusCodes.Status409Conflict),

        _ => Results.Problem(
            title: "Validation failed",
            detail: error,
            statusCode: StatusCodes.Status400BadRequest)
    };
}
