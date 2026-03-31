using WitcherHub.Application.Interfaces;

namespace WitcherHub.Configuration.Extensions
{
    public static class WebApplicationSeedingExtensions
    {
        public static async Task SeedAsync(this WebApplication app)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

            try
            {
                var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
                await seeder.SeedAsync();
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Seeding failed.");
                throw;
            }
        }
    }
}
