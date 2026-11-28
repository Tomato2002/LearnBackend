namespace LearnBackend.Shared.Contracts
{
    /// <summary>
    /// Danh sách mã lỗi chuẩn dùng chung giữa Server và Unity Client (tránh gõ sai chuỗi khi làm localization).
    /// </summary>
    public static class GameErrorCodes
    {
        public const string Unauthorized = "UNAUTHORIZED";
        public const string InternalServerError = "INTERNAL_SERVER_ERROR";
        public const string RateLimited = "RATE_LIMITED";
        public const string PlayerBusy = "PLAYER_BUSY";
        public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
        public const string ServerMaintenance = "SERVER_MAINTENANCE";
        public const string FeatureDisabled = "FEATURE_DISABLED";

        public const string InvalidCredentials = "INVALID_CREDENTIALS";
        public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
        public const string DuplicateUsername = "DUPLICATE_USERNAME";
        public const string DuplicateNickname = "DUPLICATE_NICKNAME";

        public const string InsufficientGold = "INSUFFICIENT_GOLD";
        public const string InsufficientGems = "INSUFFICIENT_GEMS";
        public const string InsufficientStamina = "INSUFFICIENT_STAMINA";
        public const string InsufficientMaterial = "INSUFFICIENT_MATERIAL";

        public const string DailyBonusAlreadyClaimed = "DAILY_BONUS_ALREADY_CLAIMED";
        public const string HourlyBonusCooldown = "HOURLY_BONUS_COOLDOWN";
    }
}
