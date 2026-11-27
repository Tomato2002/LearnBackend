using LearnBackend.Shared.Contracts;
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
    public async Task<ActionResult<ApiResponse<InventoryResponse>>> GetInventory(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var inventory = await inventoryService.GetInventoryAsync(playerId, ct);
        return Ok(ApiResponse.Ok(inventory));
    }

    [HttpPost("equipment/{equipmentId:guid}/equip")]
    public async Task<ActionResult<ApiResponse<EquipmentDto>>> Equip(Guid equipmentId, EquipRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var equipment = await inventoryService.EquipAsync(playerId, equipmentId, request.HeroId, ct);
        return Ok(ApiResponse.Ok(equipment));
    }

    [HttpPost("equipment/{equipmentId:guid}/enhance")]
    public async Task<ActionResult<ApiResponse<EquipmentDto>>> Enhance(Guid equipmentId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        var equipment = await inventoryService.EnhanceAsync(playerId, equipmentId, ct);
        return Ok(ApiResponse.Ok(equipment));
    }
}
