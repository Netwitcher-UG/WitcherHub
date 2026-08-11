using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WitcherHub.Infrastructure.Data.Context;

namespace WitcherHub.Configuration.HealthChecks
{
    /// <summary>
    /// Readiness probe: verifies the application can actually reach PostgreSQL.
    /// Kept dependency-free (no extra health-check packages) on purpose.
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _db;

        public DatabaseHealthCheck(AppDbContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _db.Database.CanConnectAsync(cancellationToken);

                return canConnect
                    ? HealthCheckResult.Healthy("Database reachable.")
                    : HealthCheckResult.Unhealthy("Database is not reachable.");
            }
            catch (Exception ex)
            {
                // The message may contain host/user details, so it is not returned
                // to the caller; it is only attached for the local logger.
                return HealthCheckResult.Unhealthy("Database connection failed.", ex);
            }
        }
    }
}
