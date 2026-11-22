namespace LearnBackend.Modules.Inventory.Entities;

/// <summary>
/// Vật phẩm cộng dồn (Stackable): Bình EXP, Đá cường hóa, Mảnh tướng...
/// Mỗi (PlayerId, ItemId) chỉ tốn đúng 1 dòng trong DB.
/// </summary>
public sealed class InventoryItem
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
