using System.Net;
using System.Security.Claims;
using LearnBackend.Common.Exceptions;

namespace LearnBackend.Common.Extensions;

public static class ClaimsPrincipalExtensions
{
    public const string PlayerIdClaimType = "player_id";

    public static Guid GetPlayerId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(PlayerIdClaimType);
        if (Guid.TryParse(raw, out var playerId))
        {
            return playerId;
        }

        throw new GameException(
            "UNAUTHORIZED",
            "Token không hợp lệ hoặc thiếu thông tin người chơi.",
            HttpStatusCode.Unauthorized);
    }

    public static bool TryGetPlayerId(this ClaimsPrincipal user, out Guid playerId)
    {
        var raw = user.FindFirstValue(PlayerIdClaimType);
        return Guid.TryParse(raw, out playerId);
    }
}
