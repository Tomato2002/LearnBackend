using LearnBackend.Shared.Contracts;
using LearnBackend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Modules.Mail;

[ApiController]
[Authorize]
[Route("api/mail")]
public sealed class MailController(MailService mailService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<MailItemDto>>>> GetInbox(CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await mailService.GetInboxAsync(playerId, ct)));
    }

    [HttpPost("{mailId:guid}/claim")]
    public async Task<ActionResult<ApiResponse<MailItemDto>>> Claim(Guid mailId, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await mailService.ClaimAsync(playerId, mailId, ct)));
    }
}
