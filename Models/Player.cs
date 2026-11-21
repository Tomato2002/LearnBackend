namespace LearnBackend.Models;

public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Exp { get; set; } = 0;
    public int Gold { get; set; } = 100; // Tân thủ tặng 100 vàng
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;

    // Thuộc tính tính toán: Lượng Exp cần để lên cấp tiếp theo (ví dụ: Level 1 cần 100 Exp, Level 2 cần 200 Exp)
    public int ExpToNextLevel => Level * 100;
}
