using LearnBackend.Common.DTOs;
using LearnBackend.Modules.Auth.DTOs;
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
    {
        var result = await authService.RegisterAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return Ok(ApiResponse.OkMessage("Đã thu hồi phiên đăng nhập."));
    }
}
