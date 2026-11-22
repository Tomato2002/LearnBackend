using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LearnBackend.Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(GameDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("Database is reachable.")
            : HealthCheckResult.Unhealthy("Cannot connect to database.");
    }
}
