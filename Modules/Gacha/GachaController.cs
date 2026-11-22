using LearnBackend.Common.DTOs;
using LearnBackend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Gacha;

[ApiController]
[Authorize]
[Route("api/gacha")]
public sealed class GachaController(GachaService gachaService) : ControllerBase
{
    [HttpGet("banners")]
    public async Task<ActionResult<ApiResponse<List<BannerStatusDto>>>> GetBanners(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await gachaService.GetBannersAsync(playerId, ct)));
    }

    [HttpPost("pull")]
    public async Task<ActionResult<ApiResponse<GachaPullResponse>>> Pull(GachaPullRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await gachaService.PullAsync(playerId, request, ct)));
    }
}
