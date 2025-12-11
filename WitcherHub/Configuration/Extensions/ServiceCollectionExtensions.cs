using WitcherHub.Application;
using WitcherHub.Infrastructure;

namespace WitcherHub.Configuration.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPresentation(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddRazorPages();
            services.AddControllers();

            services.AddApplication();
            services.AddInfrastructure(configuration);

            services.AddSwaggerDocumentation();

            return services;
        }
    }
}
