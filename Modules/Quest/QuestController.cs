using LearnBackend.Common.DTOs;
using LearnBackend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Quest;

[ApiController]
[Authorize]
[Route("api/quests")]
public sealed class QuestController(QuestService questService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<QuestStatusDto>>>> GetQuests(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await questService.GetDailyQuestsAsync(playerId, ct)));
    }

    [HttpPost("{questId}/claim")]
    public async Task<ActionResult<ApiResponse<QuestStatusDto>>> Claim(string questId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await questService.ClaimAsync(playerId, questId, ct)));
    }
}
