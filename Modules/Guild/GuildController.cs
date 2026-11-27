using LearnBackend.Shared.Contracts;
using LearnBackend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Guild;

[ApiController]
[Authorize]
[Route("api/guilds")]
public sealed class GuildController(GuildService guildService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<GuildSummaryDto>>>> ListGuilds(CancellationToken ct)
        => Ok(ApiResponse.Ok(await guildService.ListGuildsAsync(ct)));

    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<MyGuildResponse?>>> GetMyGuild(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await guildService.GetMyGuildAsync(playerId, ct)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MyGuildResponse>>> CreateGuild(CreateGuildRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await guildService.CreateGuildAsync(playerId, request, ct)));
    }

    [HttpPost("{guildId:guid}/join")]
    public async Task<ActionResult<ApiResponse<MyGuildResponse>>> JoinGuild(Guid guildId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await guildService.JoinGuildAsync(playerId, guildId, ct)));
    }

    [HttpPost("leave")]
    public async Task<ActionResult<ApiResponse<object?>>> LeaveGuild(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        await guildService.LeaveGuildAsync(playerId, ct);
        return Ok(ApiResponse.OkMessage("Đã rời Bang Hội."));
    }

    [HttpPost("boss/attack")]
    public async Task<ActionResult<ApiResponse<AttackGuildBossResponse>>> AttackBoss(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await guildService.AttackBossAsync(playerId, ct)));
    }
}
