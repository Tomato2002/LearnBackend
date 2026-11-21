using System.ComponentModel.DataAnnotations;

namespace LearnBackend.DTOs;

public class GainExpRequest
{
    [Range(1, 10000, ErrorMessage = "Lượng Exp nhận được phải từ 1 đến 10,000!")]
    public int Amount { get; set; }
}
