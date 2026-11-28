using LearnBackend.Common.Extensions;
using LearnBackend.Modules.Auth.DTOs;
using LearnBackend.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LearnBackend.Modules.Auth;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(RegisterRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await authService.RegisterAsync(request, ct)));

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await authService.LoginAsync(request, ct)));

    [HttpPost("guest")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> GuestLogin(GuestLoginRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await authService.GuestLoginAsync(request, ct)));

    [HttpPost("link")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> LinkAccount(LinkAccountRequest request, CancellationToken ct)
    {
        var playerId = User.GetPlayerId();
        return Ok(ApiResponse.Ok(await authService.LinkGuestAccountAsync(playerId, request, ct), "Liên kết tài khoản thành công!"));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(RefreshTokenRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await authService.RefreshAsync(request, ct)));

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return Ok(ApiResponse.OkMessage("Đã thu hồi phiên đăng nhập."));
    }
}
