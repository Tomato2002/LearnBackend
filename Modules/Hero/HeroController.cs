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
    public async Task<ActionResult<List<PlayerHeroDto>>> GetMyHeroes(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(await heroService.GetMyHeroesAsync(playerId, ct));
    }

    [HttpPost("{heroId:guid}/level-up")]
    public async Task<ActionResult<PlayerHeroDto>> LevelUp(Guid heroId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(await heroService.LevelUpHeroAsync(playerId, heroId, ct));
    }

    [HttpPut("team")]
    public async Task<ActionResult<List<PlayerHeroDto>>> UpdateTeam(UpdateTeamRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(await heroService.UpdateTeamAsync(playerId, request, ct));
    }
}
