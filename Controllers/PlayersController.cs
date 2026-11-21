using Microsoft.AspNetCore.Mvc;
using LearnBackend.Models;
using LearnBackend.DTOs;

namespace LearnBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
    // 1. DỮ LIỆU CẤU HÌNH QUÁI VẬT NẰM AN TOÀN TRÊN SERVER (CLIENT KHÔNG THỂ CAN THIỆP)
    private static readonly List<Monster> Monsters = new()
    {
        new Monster { Id = 1, Name = "Goblin yếu ớt", ExpReward = 40, GoldReward = 15, MinLevelRequired = 1 },
        new Monster { Id = 2, Name = "Sói Rừng Hoang Dã", ExpReward = 90, GoldReward = 45, MinLevelRequired = 2 },
        new Monster { Id = 3, Name = "Trùm Rồng Lửa (Boss)", ExpReward = 500, GoldReward = 350, MinLevelRequired = 5 }
    };

    // 2. DỮ LIỆU NHÂN VẬT TRÊN RAM
    private static readonly List<Player> Players = new()
    {
        new Player { Id = 1, Name = "Arthur", Level = 1, Exp = 20, Gold = 150, Health = 100, MaxHealth = 100 },
        new Player { Id = 2, Name = "Merlin", Level = 3, Exp = 80, Gold = 300, Health = 140, MaxHealth = 140 }
    };

    /// <summary>
    /// LẤY DANH SÁCH QUÁI VẬT ĐỂ CLIENT HIỂN THỊ TRÊN MÀN HÌNH CHỌN ẢI
    /// Endpoint: GET /api/players/monsters
    /// </summary>
    [HttpGet("monsters")]
    public ActionResult<IEnumerable<Monster>> GetMonsters()
    {
        return Ok(Monsters);
    }

    /// <summary>
    /// LẤY DANH SÁCH TẤT CẢ NHÂN VẬT
    /// Endpoint: GET /api/players
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<Player>> GetAll()
    {
        return Ok(Players);
    }

    /// <summary>
    /// LẤY CHI TIẾT 1 NHÂN VẬT THEO ID
    /// Endpoint: GET /api/players/{id}
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<Player> GetById(int id)
    {
        var player = Players.FirstOrDefault(p => p.Id == id);
        if (player == null)
        {
            return NotFound(new { message = $"Không tìm thấy nhân vật có ID = {id}!" });
        }
        return Ok(player);
    }

    /// <summary>
    /// TẠO NHÂN VẬT MỚI
    /// Endpoint: POST /api/players
    /// </summary>
    [HttpPost]
    public ActionResult<Player> Create(CreatePlayerRequest request)
    {
        bool nameExists = Players.Any(p => p.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
        if (nameExists)
        {
            return BadRequest(new { message = "Tên nhân vật này đã có người sử dụng!" });
        }

        int nextId = Players.Any() ? Players.Max(p => p.Id) + 1 : 1;

        var newPlayer = new Player
        {
            Id = nextId,
            Name = request.Name,
            Level = 1,
            Exp = 0,
            Gold = 100,
            Health = 100,
            MaxHealth = 100
        };

        Players.Add(newPlayer);
        return CreatedAtAction(nameof(GetById), new { id = newPlayer.Id }, newPlayer);
    }

    /// <summary>
    /// ĐÁNH QUÁI VẬT (CHUẨN BẢO MẬT BACKEND GAME)
    /// Client CHỈ ĐƯỢC PHÉP gửi MonsterId, Server tự quyết định Exp và Vàng thưởng!
    /// Endpoint: POST /api/players/{id}/defeat-monster
    /// </summary>
    [HttpPost("{id}/defeat-monster")]
    public ActionResult DefeatMonster(int id, DefeatMonsterRequest request)
    {
        // 1. Kiểm tra nhân vật có tồn tại không
        var player = Players.FirstOrDefault(p => p.Id == id);
        if (player == null)
        {
            return NotFound(new { message = $"Không tìm thấy nhân vật ID = {id}!" });
        }

        // 2. Kiểm tra quái vật có trong hệ thống không (Chống client bịa ra ID quái lạ)
        var monster = Monsters.FirstOrDefault(m => m.Id == request.MonsterId);
        if (monster == null)
        {
            return BadRequest(new { message = $"Quái vật ID = {request.MonsterId} không tồn tại trong game!" });
        }

        // 3. CHỐNG HACK CẤP ĐỘ: Kiểm tra nhân vật có đủ cấp để đánh quái này không
        if (player.Level < monster.MinLevelRequired)
        {
            return BadRequest(new
            {
                message = $"Cảnh báo gian lận! Bạn đang Level {player.Level}, nhưng quái vật '{monster.Name}' yêu cầu tối thiểu Level {monster.MinLevelRequired}!"
            });
        }

        // 4. SERVER TỰ CỘNG THƯỞNG (Không ai hack được số liệu này)
        player.Exp += monster.ExpReward;
        player.Gold += monster.GoldReward;

        // 5. XỬ LÝ LÊN CẤP (LEVEL UP)
        int levelsGained = 0;
        while (player.Exp >= player.ExpToNextLevel)
        {
            player.Exp -= player.ExpToNextLevel;
            player.Level++;
            player.MaxHealth += 20;
            player.Health = player.MaxHealth; // Hồi đầy máu
            levelsGained++;
        }

        // 6. TRẢ KẾT QUẢ CHO CLIENT
        return Ok(new
        {
            success = true,
            message = levelsGained > 0 
                ? $"Tiêu diệt thành công '{monster.Name}'! Bạn đã thăng {levelsGained} cấp lên Level {player.Level}!" 
                : $"Tiêu diệt thành công '{monster.Name}'!",
            rewards = new
            {
                expEarned = monster.ExpReward,
                goldEarned = monster.GoldReward
            },
            leveledUp = levelsGained > 0,
            playerState = player
        });
    }
}
