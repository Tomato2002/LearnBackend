using LearnBackend.Common.Extensions;
using LearnBackend.Modules.Inventory.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Inventory;

[ApiController]
[Authorize]
[Route("api/inventory")]
public sealed class InventoryController(InventoryService inventoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InventoryResponse>> GetInventory(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(await inventoryService.GetInventoryAsync(playerId, ct));
    }

    [HttpPost("equipment/{equipmentId:guid}/equip")]
    public async Task<ActionResult<EquipmentDto>> Equip(Guid equipmentId, EquipRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(await inventoryService.EquipAsync(playerId, equipmentId, request.HeroId, ct));
    }

    [HttpPost("equipment/{equipmentId:guid}/enhance")]
    public async Task<ActionResult<EquipmentDto>> Enhance(Guid equipmentId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(await inventoryService.EnhanceAsync(playerId, equipmentId, ct));
    }
}
