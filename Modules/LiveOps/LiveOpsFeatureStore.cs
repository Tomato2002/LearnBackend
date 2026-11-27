using LearnBackend.Shared.Contracts;

namespace LearnBackend.Modules.LiveOps;

/// <summary>
/// Lưu trạng thái Feature Flags & Maintenance Mode trên RAM (thread-safe).
/// Cho phép GM bật/tắt nóng bất kỳ tính năng nào (Gacha, Shop, Arena, Guild, Battle...)
/// mà không cần build lại hay khởi động lại server!
/// </summary>
public sealed class LiveOpsFeatureStore
{
    private readonly object _gate = new();
    private FeatureFlagsDto _flags = new();

    public FeatureFlagsDto GetSnapshot()
    {
        lock (_gate)
        {
            return new FeatureFlagsDto
            {
                MaintenanceMode = _flags.MaintenanceMode,
                MaintenanceMessage = _flags.MaintenanceMessage,
                EnableBattle = _flags.EnableBattle,
                EnableGacha = _flags.EnableGacha,
                EnableShop = _flags.EnableShop,
                EnableArena = _flags.EnableArena,
                EnableGuild = _flags.EnableGuild,
                EnableDailyBonus = _flags.EnableDailyBonus,
                EnableHourlyBonus = _flags.EnableHourlyBonus
            };
        }
    }

    public FeatureFlagsDto Update(FeatureFlagsDto updated)
    {
        lock (_gate)
        {
            _flags = new FeatureFlagsDto
            {
                MaintenanceMode = updated.MaintenanceMode,
                MaintenanceMessage = string.IsNullOrWhiteSpace(updated.MaintenanceMessage)
                    ? "Server đang bảo trì định kỳ, vui lòng quay lại sau."
                    : updated.MaintenanceMessage.Trim(),
                EnableBattle = updated.EnableBattle,
                EnableGacha = updated.EnableGacha,
                EnableShop = updated.EnableShop,
                EnableArena = updated.EnableArena,
                EnableGuild = updated.EnableGuild,
                EnableDailyBonus = updated.EnableDailyBonus,
                EnableHourlyBonus = updated.EnableHourlyBonus
            };

            return GetSnapshot();
        }
    }
}
