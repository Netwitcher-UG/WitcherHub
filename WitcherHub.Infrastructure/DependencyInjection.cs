
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System;
using System.Net.Http.Headers;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Authentication;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Seeding;
using WitcherHub.Infrastructure.Services.Lexware;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
            });

            // ===== Identity =====
            services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 6;
            })
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<AppDbContext>();

            //======= Lexware =======
            services.Configure<LexwareOptions>(
                configuration.GetSection(LexwareOptions.SectionName));

            services.AddHttpClient<ILexwareClient, LexwareClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<LexwareOptions>>().Value;

                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.AccessToken);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.Configure<OpenAIOptions>(
            configuration.GetSection(OpenAIOptions.SectionName));

            services.AddSingleton<ChatClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;

                var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
                    ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    : options.ApiKey;

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        "OpenAI API key is not configured. ");
                }

                var model = string.IsNullOrWhiteSpace(options.Model)
                    ? "gpt-4o"
                    : options.Model;

                return new ChatClient(model, apiKey);
            });

            services.AddScoped<IAiTextGenerator, OpenAiTextGenerator>();
            services.AddScoped<IDataSeeder, IdentityDataSeeder>();
            services.AddScoped<IAuthService, AuthService>();

            return services;
        }
    }
}
