using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Shared.Contracts;

namespace LearnBackend.Modules.LiveOps;

/// <summary>
/// Middleware "cầu dao tổng" LiveOps:
/// - Nếu MaintenanceMode = true -> chặn toàn bộ API gameplay (trả 503 SERVER_MAINTENANCE),
///   nhưng vẫn mở /api/liveops/bootstrap, /api/liveops/flags, /health và file tĩnh HTML.
/// - Nếu một Feature Flag bị tắt (VD: EnableGacha = false) -> chặn ngay endpoint /api/gacha/* với 503 FEATURE_DISABLED.
/// </summary>
public sealed class LiveOpsGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, LiveOpsFeatureStore featureStore)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Luôn cho phép các cổng quản trị LiveOps, đăng nhập GM, Health Check, Scalar và tài nguyên tĩnh
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/liveops/bootstrap", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/liveops/flags", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/liveops/admin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var flags = featureStore.GetSnapshot();

        if (flags.MaintenanceMode)
        {
            throw new GameException(
                GameErrorCodes.ServerMaintenance,
                flags.MaintenanceMessage,
                HttpStatusCode.ServiceUnavailable);
        }

        if (!flags.EnableBattle && path.StartsWith("/api/battle", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Chiến Dịch (Battle)");

        if (!flags.EnableGacha && path.StartsWith("/api/gacha", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Triệu Hồi (Gacha)");

        if (!flags.EnableShop && path.StartsWith("/api/shop", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Cửa Hàng (Shop)");

        if (!flags.EnableArena && path.StartsWith("/api/arena", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Đấu Trường (Arena)");

        if (!flags.EnableGuild && path.StartsWith("/api/guilds", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Bang Hội (Guild)");

        if (!flags.EnableDailyBonus && path.StartsWith("/api/liveops/daily-bonus", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Điểm Danh 7 Ngày");

        if (!flags.EnableHourlyBonus && path.StartsWith("/api/liveops/hourly-bonus", StringComparison.OrdinalIgnoreCase))
            ThrowDisabled("Rương Quà Theo Giờ");

        await next(context);
    }

    private static void ThrowDisabled(string featureName)
    {
        throw new GameException(
            GameErrorCodes.FeatureDisabled,
            $"Tính năng '{featureName}' đang tạm đóng bởi LiveOps để cân bằng/bảo trì.",
            HttpStatusCode.ServiceUnavailable);
    }
}
