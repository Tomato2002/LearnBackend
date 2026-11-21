namespace LearnBackend.Models;

public class Monster
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ExpReward { get; set; }       // Lượng Exp Server quyết định thưởng
    public int GoldReward { get; set; }      // Lượng Vàng Server quyết định thưởng
    public int MinLevelRequired { get; set; } // Cấp tối thiểu để được đánh quái này (chống hack)
}
