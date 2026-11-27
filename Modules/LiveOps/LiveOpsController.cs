using LearnBackend.Common.Extensions;
using LearnBackend.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.LiveOps;

[ApiController]
[Route("api/liveops")]
public sealed class LiveOpsController(
    LiveOpsService liveOpsService,
    LiveOpsFeatureStore featureStore) : ControllerBase
{
    /// <summary>
    /// Điểm vào đầu tiên khi mở game Unity (Dynamic Discovery + Feature Flags + Maintenance + ServerTimeUtc).
    /// </summary>
    [HttpGet("bootstrap")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LiveOpsBootstrapResponse>> Bootstrap()
        => Ok(ApiResponse.Ok(liveOpsService.GetBootstrap()));

    /// <summary>
    /// Bật/tắt nóng Feature Flags & Maintenance Mode (GM/Admin LiveOps Control).
    /// </summary>
    [HttpPost("flags")]
    [Authorize]
    public ActionResult<ApiResponse<FeatureFlagsDto>> UpdateFlags([FromBody] FeatureFlagsDto flags)
        => Ok(ApiResponse.Ok(featureStore.Update(flags), "Đã cập nhật Feature Flags nóng thành công!"));

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
