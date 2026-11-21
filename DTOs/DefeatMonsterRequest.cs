using System.ComponentModel.DataAnnotations;

namespace LearnBackend.DTOs;

public class DefeatMonsterRequest
{
    [Required(ErrorMessage = "MonsterId không được để trống!")]
    [Range(1, int.MaxValue, ErrorMessage = "MonsterId phải lớn hơn 0!")]
    public int MonsterId { get; set; }
}
