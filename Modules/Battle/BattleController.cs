using LearnBackend.Common.DTOs;
using LearnBackend.Common.Extensions;
using LearnBackend.Modules.Battle.DTOs;
using LearnBackend.Modules.Battle.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Battle;

[ApiController]
[Authorize]
[Route("api/battle")]
public sealed class BattleController(
    BattleService battleService,
    LuaSkillRunner luaSkillRunner) : ControllerBase
{
    /// <summary>
    /// Vào ải đánh theo lượt (Server chạy Lua Skills, trừ Stamina, cộng Gold/EXP/Loot trong 1 Transaction).
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<BattleRunResponse>>> RunStage(RunBattleRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var result = await battleService.RunStageAsync(playerId, request.StageId, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>
    /// Xem danh sách các file Script Lua Skill đang được load trong Sandbox.
    /// </summary>
    [HttpGet("scripts")]
    [AllowAnonymous]
    public IActionResult GetLuaScripts()
    {
        var scripts = luaSkillRunner.GetLoadedScripts()
            .Select(kv => new { skillId = kv.Key, source = kv.Value })
            .OrderBy(x => x.skillId);
        return Ok(ApiResponse.Ok(scripts));
    }
}
