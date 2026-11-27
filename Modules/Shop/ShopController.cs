using LearnBackend.Shared.Contracts;
using LearnBackend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Shop;

[ApiController]
[Authorize]
[Route("api/shop")]
public sealed class ShopController(ShopService shopService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ShopItemStatusDto>>>> GetShop(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await shopService.GetShopAsync(playerId, ct)));
    }

    [HttpPost("buy")]
    public async Task<ActionResult<ApiResponse<BuyShopItemResponse>>> Buy(BuyShopItemRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await shopService.BuyAsync(playerId, request, ct)));
    }
}
