using System.Net;
using System.Text.Json;
using LearnBackend.Common.Exceptions;
using LearnBackend.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Common.Middleware;

/// <summary>
/// Bắt tập trung mọi Exception trong pipeline để trả về chuẩn ApiResponse thống nhất cho Unity Client.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (GameException ex)
        {
            logger.LogWarning("Game error [{ErrorCode}]: {Message}", ex.ErrorCode, ex.Message);
            await WriteErrorAsync(context, ex.StatusCode, ex.ErrorCode, ex.Message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Optimistic concurrency conflict detected at database layer");
            await WriteErrorAsync(
                context,
                HttpStatusCode.Conflict,
                GameErrorCodes.ConcurrencyConflict,
                "Dữ liệu vừa thay đổi bởi một thao tác đồng thời khác, vui lòng thử lại.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled server exception");
            await WriteErrorAsync(
                context,
                HttpStatusCode.InternalServerError,
                GameErrorCodes.InternalServerError,
                "Đã xảy ra lỗi hệ thống trên server.");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string errorCode,
        string message)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var payload = ApiResponse.Fail(errorCode, message);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
