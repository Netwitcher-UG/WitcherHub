using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using WitcherHub.Application;
using WitcherHub.Configuration.Authorization;
using WitcherHub.Infrastructure;
using WitcherHub.Infrastructure.Authentication;

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
            services.AddAuthorization();
            services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        RoleClaimType = ClaimTypes.Role
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            if (context.Request.Headers.ContainsKey("Authorization"))
                                return Task.CompletedTask;

                            if (!context.Request.Path.StartsWithSegments("/api") &&
                                context.Request.Cookies.TryGetValue("access_token", out var token))
                            {
                                context.Token = token;
                            }

                            return Task.CompletedTask;
                        }
                    };

                });
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

            services.AddApplication();
            services.AddInfrastructure(configuration);

            services.AddSwaggerDocumentation();

            return services;
        }
    }
}
