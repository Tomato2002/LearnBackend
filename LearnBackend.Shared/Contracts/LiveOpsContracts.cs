using System;
using System.Collections.Generic;

namespace LearnBackend.Shared.Contracts
{
    /// <summary>
    /// Bảng công tắc tính năng LiveOps (bật/tắt nóng không cần restart server hay ép cập nhật bản build Unity).
    /// </summary>
    public sealed class FeatureFlagsDto
    {
        public bool MaintenanceMode { get; set; }
        public string MaintenanceMessage { get; set; } = "Server đang bảo trì định kỳ, vui lòng quay lại sau.";
        public bool EnableBattle { get; set; } = true;
        public bool EnableGacha { get; set; } = true;
        public bool EnableShop { get; set; } = true;
        public bool EnableArena { get; set; } = true;
        public bool EnableGuild { get; set; } = true;
        public bool EnableDailyBonus { get; set; } = true;
        public bool EnableHourlyBonus { get; set; } = true;
    }

    /// <summary>
    /// Kết quả GET /api/liveops/bootstrap (nâng cấp từ GET /api/index của Liveops-Backend):
    /// Unity chỉ cần biết 1 URL này để lấy danh sách endpoint động, Feature Flags và đồng bộ giờ Server.
    /// </summary>
    public sealed class LiveOpsBootstrapResponse
    {
        public string Status { get; set; } = "OK";
        public string MasterDataVersion { get; set; } = "1.0.0";
        public DateTime ServerTimeUtc { get; set; }
        public FeatureFlagsDto FeatureFlags { get; set; } = new FeatureFlagsDto();
        public Dictionary<string, string> Endpoints { get; set; } = new Dictionary<string, string>();
    }

    public sealed class DailyBonusDayDto
    {
        public int Day { get; set; }
        public long Gold { get; set; }
        public long Gems { get; set; }
        public string? ItemId { get; set; }
        public int ItemQty { get; set; }
    }

    public sealed class DailyBonusStatusResponse
    {
        public int CurrentStreakDay { get; set; }
        public int NextClaimDay { get; set; }
        public bool CanClaimToday { get; set; }
        public string? LastClaimedDateUtc { get; set; }
        public List<DailyBonusDayDto> Schedule { get; set; } = new List<DailyBonusDayDto>();
    }

    public sealed class HourlyBonusStatusResponse
    {
        public bool CanClaimNow { get; set; }
        public int CooldownSecondsRemaining { get; set; }
        public int CooldownIntervalSeconds { get; set; }
        public long RewardGold { get; set; }
        public long RewardGems { get; set; }
        public DateTime? LastClaimedAtUtc { get; set; }
    }

    public sealed class LiveOpsBonusClaimResponse
    {
        public string BonusType { get; set; } = string.Empty; // "DailyBonus" | "HourlyBonus"
        public int? ClaimedDay { get; set; }
        public long GoldEarned { get; set; }
        public long GemsEarned { get; set; }
        public string? ItemId { get; set; }
        public int ItemQty { get; set; }
        public long BalanceGold { get; set; }
        public long BalanceGems { get; set; }
    }
}
