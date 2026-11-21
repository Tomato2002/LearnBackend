using System.ComponentModel.DataAnnotations;

namespace LearnBackend.DTOs;

public class CreatePlayerRequest
{
    // Bắt buộc phải có tên, độ dài từ 3 đến 20 ký tự
    [Required(ErrorMessage = "Tên nhân vật không được để trống!")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Tên nhân vật phải từ 3 đến 20 ký tự!")]
    public string Name { get; set; } = string.Empty;
}
