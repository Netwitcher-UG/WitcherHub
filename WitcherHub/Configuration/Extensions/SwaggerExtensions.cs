using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace WitcherHub.Configuration.Extensions
{
    public static class SwaggerExtensions
    {
        private const string BearerSchemeId = "Bearer"; 

        public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "WitcherHub API",
                    Version = "v1"
                });

                options.AddSecurityDefinition(BearerSchemeId, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",        
                    BearerFormat = "JWT",
                    Description = "Paste ONLY the JWT token. Swagger UI will add 'Bearer ' automatically."
                });

                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerSchemeId, document)] = []
                });
            });

            return services;
        }

        public static IApplicationBuilder UseSwaggerDocumentation(this IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "WitcherHub API v1");
            });

            return app;
        }
    }
}
