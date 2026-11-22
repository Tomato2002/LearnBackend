namespace LearnBackend.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "LearnBackend.GameServer";
    public string Audience { get; init; } = "LearnBackend.UnityClient";
    public string SecretKey { get; init; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; init; } = 30;
    public int RefreshTokenExpirationDays { get; init; } = 14;
}
