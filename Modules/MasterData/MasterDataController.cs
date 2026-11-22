using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.MasterData;

[ApiController]
[Route("api/master-data")]
public sealed class MasterDataController(MasterDataStore masterData) : ControllerBase
{
    /// <summary>
    /// Client Unity tải toàn bộ config tĩnh (luật game, danh sách tướng, danh sách ải) khi mở game.
    /// </summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(new
        {
            gameRules = masterData.GameRules,
            heroes = masterData.Heroes.Values,
            stages = masterData.Stages.Values
        });
    }

    [HttpGet("heroes")]
    public IActionResult GetHeroes() => Ok(masterData.Heroes.Values);

    [HttpGet("stages")]
    public IActionResult GetStages() => Ok(masterData.Stages.Values);
}
