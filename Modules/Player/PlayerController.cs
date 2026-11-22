using LearnBackend.Common.DTOs;
using LearnBackend.Common.Extensions;
using LearnBackend.Modules.Player.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Player;

[ApiController]
[Authorize]
[Route("api/player")]
public sealed class PlayerController(PlayerService playerService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<PlayerProfileResponse>>> GetMe(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var profile = await playerService.GetProfileAsync(playerId, ct);
        return Ok(ApiResponse.Ok(profile));
    }

    [HttpPost("stamina/refill")]
    public async Task<ActionResult<ApiResponse<PlayerProfileResponse>>> RefillStamina(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var profile = await playerService.RefillStaminaAsync(playerId, ct);
        return Ok(ApiResponse.Ok(profile));
    }

    [HttpPost("stamina/spend")]
    public async Task<ActionResult<ApiResponse<PlayerProfileResponse>>> SpendStamina(SpendStaminaRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var profile = await playerService.SpendStaminaAsync(playerId, request.Amount, ct);
        return Ok(ApiResponse.Ok(profile));
    }

    [HttpGet("ledger")]
    public async Task<ActionResult<ApiResponse<List<CurrencyLedgerDto>>>> GetLedger([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var playerId = User.GetPlayerId();
        var ledger = await playerService.GetCurrencyLedgerAsync(playerId, limit, ct);
        return Ok(ApiResponse.Ok(ledger));
    }
}
