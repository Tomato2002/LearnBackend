using System.ComponentModel.DataAnnotations;
using LearnBackend.Common.Extensions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Mail.Entities;
using LearnBackend.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.LiveOps;

public sealed class SendGlobalMailRequest
{
    [Required, MaxLength(128)]
    public string Title { get; init; } = string.Empty;

    [Required, MaxLength(512)]
    public string Body { get; init; } = string.Empty;

    [Range(0, 1_000_000)]
    public long RewardGold { get; init; }

    [Range(0, 100_000)]
    public long RewardGems { get; init; }

    [MaxLength(64)]
    public string? RewardItemId { get; init; }

    [Range(0, 1000)]
    public int RewardItemQty { get; init; }

    [Range(1, 365)]
    public int ExpireDays { get; init; } = 30;
}

[ApiController]
[Route("api/liveops")]
public sealed class LiveOpsController(
    LiveOpsService liveOpsService,
    LiveOpsFeatureStore featureStore,
    GameDbContext db) : ControllerBase
{
    [HttpGet("bootstrap")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LiveOpsBootstrapResponse>> Bootstrap()
        => Ok(ApiResponse.Ok(liveOpsService.GetBootstrap()));

    /// <summary>
    /// CHỈ TÀI KHOẢN GM / ADMIN ([Authorize(Roles = "Admin")]) mới được bật/tắt nóng Feature Flags & Maintenance Mode!
    /// </summary>
    [HttpPost("flags")]
    [Authorize(Roles = "Admin")]
    public ActionResult<ApiResponse<FeatureFlagsDto>> UpdateFlags([FromBody] FeatureFlagsDto flags)
        => Ok(ApiResponse.Ok(featureStore.Update(flags), "Đã cập nhật Feature Flags nóng thành công!"));

    /// <summary>
    /// GM / Admin phát thư quà đền bù toàn server O(1) (chỉ insert 1 dòng vào GlobalMails).
    /// </summary>
    [HttpPost("admin/global-mail")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<object>>> SendGlobalMail(SendGlobalMailRequest request, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var mail = new GlobalMail
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            RewardGold = request.RewardGold,
            RewardGems = request.RewardGems,
            RewardItemId = string.IsNullOrWhiteSpace(request.RewardItemId) ? null : request.RewardItemId.Trim(),
            RewardItemQty = request.RewardItemQty,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddDays(request.ExpireDays)
        };

        db.GlobalMails.Add(mail);
        await db.SaveChangesAsync(ct);

        return Ok(ApiResponse.Ok<object>(new { mail.Id, mail.Title }, "Đã phát thư toàn server thành công!"));
    }

    [HttpGet("daily-bonus")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<DailyBonusStatusResponse>>> GetDailyBonus(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await liveOpsService.GetDailyBonusStatusAsync(playerId, ct)));
    }

    [HttpPost("daily-bonus/claim")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<LiveOpsBonusClaimResponse>>> ClaimDailyBonus(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await liveOpsService.ClaimDailyBonusAsync(playerId, ct)));
    }

    [HttpGet("hourly-bonus")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<HourlyBonusStatusResponse>>> GetHourlyBonus(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await liveOpsService.GetHourlyBonusStatusAsync(playerId, ct)));
    }

    [HttpPost("hourly-bonus/claim")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<LiveOpsBonusClaimResponse>>> ClaimHourlyBonus(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await liveOpsService.ClaimHourlyBonusAsync(playerId, ct)));
    }
}
