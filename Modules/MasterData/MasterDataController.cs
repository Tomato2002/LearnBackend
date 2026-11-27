using LearnBackend.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.MasterData;

[ApiController]
[Route("api/master-data")]
public sealed class MasterDataController(MasterDataStore masterData) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(ApiResponse.Ok(new
        {
            gameRules = masterData.GameRules,
            heroes = masterData.Heroes.Values,
            enemies = masterData.Enemies.Values,
            items = masterData.Items.Values,
            stages = masterData.Stages.Values,
            banners = masterData.Banners.Values,
            shopItems = masterData.ShopItems.Values,
            quests = masterData.Quests.Values
        }));
    }

    [HttpGet("heroes")]
    public IActionResult GetHeroes() => Ok(ApiResponse.Ok(masterData.Heroes.Values));

    [HttpGet("enemies")]
    public IActionResult GetEnemies() => Ok(ApiResponse.Ok(masterData.Enemies.Values));

    [HttpGet("items")]
    public IActionResult GetItems() => Ok(ApiResponse.Ok(masterData.Items.Values));

    [HttpGet("stages")]
    public IActionResult GetStages() => Ok(ApiResponse.Ok(masterData.Stages.Values));
}
