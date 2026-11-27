using LearnBackend.Shared.Contracts;
using LearnBackend.Common.Extensions;
using LearnBackend.Modules.Hero.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Hero;

[ApiController]
[Authorize]
[Route("api/heroes")]
public sealed class HeroController(HeroService heroService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<PlayerHeroDto>>>> GetMyHeroes(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var heroes = await heroService.GetMyHeroesAsync(playerId, ct);
        return Ok(ApiResponse.Ok(heroes));
    }

    [HttpPost("{heroId:guid}/level-up")]
    public async Task<ActionResult<ApiResponse<PlayerHeroDto>>> LevelUp(Guid heroId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var hero = await heroService.LevelUpHeroAsync(playerId, heroId, ct);
        return Ok(ApiResponse.Ok(hero));
    }

    [HttpPut("team")]
    public async Task<ActionResult<ApiResponse<List<PlayerHeroDto>>>> UpdateTeam(UpdateTeamRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var team = await heroService.UpdateTeamAsync(playerId, request, ct);
        return Ok(ApiResponse.Ok(team));
    }
}
