using LearnBackend.Common.Extensions;
using LearnBackend.Common.Locking;

namespace LearnBackend.Common.Middleware;

/// <summary>
/// Tự động khóa các HTTP request có khả năng thay đổi trạng thái (POST, PUT, PATCH, DELETE)
/// theo từng PlayerId đã đăng nhập.
/// </summary>
public sealed class PlayerLockMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public async Task InvokeAsync(HttpContext context, IPlayerLockManager lockManager)
    {
        if (MutatingMethods.Contains(context.Request.Method) &&
            context.User.Identity?.IsAuthenticated == true &&
            context.User.TryGetPlayerId(out var playerId))
        {
            using var _ = await lockManager.AcquireAsync(playerId, context.RequestAborted);
            await next(context);
            return;
        }

        await next(context);
    }
}
