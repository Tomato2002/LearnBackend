using LearnBackend.Common.DTOs;
using LearnBackend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Arena;

[ApiController]
[Authorize]
[Route("api/arena")]
public sealed class ArenaController(ArenaService arenaService) : ControllerBase
{
    [HttpGet("leaderboard")]
    public async Task<ActionResult<ApiResponse<ArenaLeaderboardResponse>>> GetLeaderboard(
        [FromQuery] int top = 20,
        CancellationToken ct = default)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await arenaService.GetLeaderboardAsync(playerId, top, ct)));
    }

    [HttpGet("opponents")]
    public async Task<ActionResult<ApiResponse<List<ArenaOpponentDto>>>> GetOpponents(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await arenaService.GetOpponentsAsync(playerId, ct)));
    }

    [HttpPost("challenge")]
    public async Task<ActionResult<ApiResponse<ArenaChallengeResponse>>> Challenge(
        ArenaChallengeRequest request,
        CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await arenaService.ChallengeAsync(playerId, request, ct)));
    }
}
