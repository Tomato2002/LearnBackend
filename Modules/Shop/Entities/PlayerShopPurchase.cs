namespace LearnBackend.Modules.Shop.Entities;

/// <summary>
/// Theo dõi số lần mua hàng trong chu kỳ (PeriodKey = "yyyy-MM-dd" UTC).
/// Không cần cron job reset lúc nửa đêm — qua ngày mới PeriodKey đổi là tự động reset giới hạn!
/// </summary>
public sealed class PlayerShopPurchase
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public string ShopItemId { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public int PurchasedCount { get; set; }
}
