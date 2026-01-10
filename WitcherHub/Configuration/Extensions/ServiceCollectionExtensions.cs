using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using WitcherHub.Application;
using WitcherHub.Configuration.Authorization;
using WitcherHub.Infrastructure;
using WitcherHub.Infrastructure.Authentication;
using WitcherHub.Resources;



namespace WitcherHub.Configuration.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPresentation(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddLocalization();

            services.AddRazorPages()
                .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
                .AddDataAnnotationsLocalization(options =>
                {
                    options.DataAnnotationLocalizerProvider = (type, factory) =>
                        factory.Create(typeof(SharedResource));
                });

            services.AddControllers()
                .AddJsonOptions(o =>
                {
                    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                })
                .AddDataAnnotationsLocalization(options =>
                {
                    options.DataAnnotationLocalizerProvider = (type, factory) =>
                        factory.Create(typeof(SharedResource));
                });

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
                        },

                        OnChallenge = context =>
                        {
                            if (!context.Request.Path.StartsWithSegments("/api") &&
                                !context.Request.Path.StartsWithSegments("/swagger"))
                            {
                                context.HandleResponse(); 
                                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                                context.Response.Redirect($"/Auth/Login?returnUrl={returnUrl}");
                            }

                            return Task.CompletedTask;
                        },

                        OnForbidden = context =>
                        {
                            if (!context.Request.Path.StartsWithSegments("/api") &&
                                !context.Request.Path.StartsWithSegments("/swagger"))
                            {
                                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                                context.Response.Redirect($"/Auth/Login?returnUrl={returnUrl}");
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
