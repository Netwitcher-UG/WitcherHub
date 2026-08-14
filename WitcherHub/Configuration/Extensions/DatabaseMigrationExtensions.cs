using Microsoft.EntityFrameworkCore;
using WitcherHub.Infrastructure.Data.Context;

namespace WitcherHub.Configuration.Extensions
{
    /// <summary>
    /// Brings the database schema up to date before anything uses it.
    ///
    /// The application previously never applied migrations — they had to be run by
    /// hand against each environment. A migration that nobody remembered to apply
    /// stayed invisible until some code touched the missing table, which is exactly
    /// how the DataProtectionKeys table came to be missing while the application
    /// depended on it during start-up.
    ///
    /// Running here keeps the schema and the deployed code in step by construction.
    /// </summary>
    public static class DatabaseMigrationExtensions
    {
        public static async Task MigrateDatabaseAsync(this WebApplication app)
        {
            var logger = app.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("WitcherHub.Migrations");

            // Escape hatch for a deployment that applies migrations separately, or
            // for a replica that should not race its siblings.
            if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
            {
                logger.LogInformation(
                    "Skipping migrations because Database__MigrateOnStartup is false. " +
                    "The schema must be brought up to date another way.");
                return;
            }

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

                if (pending.Count == 0)
                {
                    logger.LogInformation("Database schema is up to date.");
                    return;
                }

                logger.LogInformation(
                    "Applying {Count} pending migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));

                await db.Database.MigrateAsync();

                logger.LogInformation("Database schema is now up to date.");
            }
            catch (Exception ex)
            {
                // Serving traffic against a half-built schema produces confusing
                // failures much later. Stop here, with the reason stated plainly.
                logger.LogCritical(
                    ex,
                    "Could not bring the database schema up to date. The application will not start. " +
                    "Check that the connection string points at the intended database and that the " +
                    "account may create tables.");

                throw;
            }
        }
    }
}
