
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System;
using System.Net.Http.Headers;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Authentication;
using WitcherHub.Infrastructure.Common.Caching;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using WitcherHub.Infrastructure.ManageData.Customers;
using WitcherHub.Infrastructure.ManageData.Invoices;
using WitcherHub.Infrastructure.ManageData.Payments;
using WitcherHub.Infrastructure.ManageData.Projects;
using WitcherHub.Infrastructure.ManageData.Registers;
using WitcherHub.Infrastructure.ManageData.Quotes;
using WitcherHub.Infrastructure.ManageData.Services;
using WitcherHub.Infrastructure.Repositories.Implementations;
using WitcherHub.Infrastructure.Seeding;
using WitcherHub.Infrastructure.Services.BackgroundTasks;
using WitcherHub.Infrastructure.Services.Caching;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Email_Sender.EmailTemplates;
using WitcherHub.Infrastructure.Services.Email_Sender.Options;
using WitcherHub.Infrastructure.Services.Email_Sender.Sender;
using WitcherHub.Infrastructure.Services.HostedServices;
using WitcherHub.Infrastructure.Services.Invoices;
using WitcherHub.Infrastructure.Services.Lexware;
using WitcherHub.Infrastructure.Services.OpenAI;
using WitcherHub.Infrastructure.Services.Pdf;
using WitcherHub.Infrastructure.Services.Quotes;
using QueuedHostedService = WitcherHub.Infrastructure.Services.BackgroundTasks.QueuedHostedService;

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

            // =========================================================
            // Data Protection
            //
            // Antiforgery tokens, password reset links and every other protected
            // payload are encrypted with this key ring. By default it is written to
            // the container filesystem, which a hosted deploy replaces on every
            // release — so a form loaded before a deploy failed with HTTP 400 after
            // it, and a reset link emailed before a deploy stopped working.
            //
            // Persisting to PostgreSQL keeps the keys across deploys and shares them
            // between instances. The application name is fixed so a rename cannot
            // silently start a new key ring.
            // =========================================================
            services.AddDataProtection()
                .PersistKeysToDbContext<AppDbContext>()
                .SetApplicationName("WitcherHub");

            // Memory cache (in-process)
            services.AddMemoryCache();
            // AppCache (our hybrid cache wrapper)
            services.AddScoped<IAppCache, AppCache>();

            // ===== Identity =====
            services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 6;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            // Required for password-reset tokens. Without it,
            // GeneratePasswordResetTokenAsync throws "No IUserTwoFactorTokenProvider
            // named 'Default' is registered".
            .AddDefaultTokenProviders();

            // Password reset links expire well before Identity's one-day default.
            services.Configure<DataProtectionTokenProviderOptions>(options =>
            {
                options.TokenLifespan = TimeSpan.FromHours(2);
            });

            //======= Lexware =======
      
            services.Configure<LexwareOptions>(configuration.GetSection(LexwareOptions.SectionName));

            // سجّل LexwareClient كـ typed client
            services.AddHttpClient<LexwareClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<LexwareOptions>>().Value;

                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.AccessToken);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.AddHostedService<RecurringInvoiceHostedService>();
            // وخلي ILexwareClient يرجّع نفس LexwareClient (حتى LexwareSyncService يضل شغال)
            services.AddScoped<ILexwareClient>(sp => sp.GetRequiredService<LexwareClient>());
            services.AddScoped<LexwareInvoiceSyncService>();
            services.AddScoped<InvoicePublicLinkService>();
            services.Configure<LexwareWebhookOptions>(
        configuration.GetSection(LexwareWebhookOptions.SectionName));

            services.AddScoped<LexwareInvoiceStatusSyncService>();
            //======= OpenAI =======
            services.Configure<OpenAIOptions>(
                configuration.GetSection(OpenAIOptions.SectionName));

            services.AddSingleton<ChatClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;

                var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
                    ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    : options.ApiKey;

                // No fallback model here any more.
                //
                // This used to substitute a hard-coded name when the configured
                // one was empty, which meant a deployment with no OpenAI__Model
                // silently called something nobody had chosen — and the resulting
                // 404 read as a model problem rather than as the configuration
                // problem it was. The model now comes from configuration or the
                // call is refused, and the refusal names the setting.
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(options.Model))
                {
                    throw new InvalidOperationException(
                        "The assistant is not configured. Missing: " +
                        string.Join(", ", options.MissingSettings));
                }

                return new ChatClient(options.Model, apiKey);
            });

            // ===== Services =====
            services.AddScoped<IAiTextGenerator, OpenAiTextGenerator>();
            services.AddScoped<IAiPositionOrganizer, AiPositionOrganizer>();
            // The semantic analyser is what reads supplied documents now. The older
            // fixed-field one is kept registered but nothing injects it: its parsing
            // helpers are still used, and keeping it resolvable means the previous
            // behaviour can be compared against the new one on a real document.
            // Delete it once the semantic pipeline has been exercised against real
            // contracts with a working API key.
            services.AddScoped<IContractTextAnalyzer, ContractTextAnalyzer>();
            services.AddScoped<ISemanticContractAnalyzer, SemanticContractAnalyzer>();
            services.AddScoped<IDataSeeder, IdentityDataSeeder>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ISignInDiagnostics, SignInDiagnostics>();
            services.AddScoped<ICustomer, ManageCustomer>();
            services.AddScoped<ILexwareSyncService, LexwareSyncService>();
            services.AddScoped<IServiceCatalog, ManageServiceCatalog>();
            services.AddScoped<IProject, ManageProject>();
            services.AddScoped<IQuote, ManageQuote>();
            services.AddScoped<IInvoice, ManageInvoice>();
            services.AddScoped<IContract, ManageContract>();

            // Reads across every project, and the money side the per-project
            // services never covered.
            services.AddScoped<IDocumentRegister, DocumentRegister>();
            services.AddScoped<IPayments, ManagePayments>();
            services.AddScoped<IContractPositions, ManageContractPositions>();
            // UnitOfWork
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // =========================================================
            // ✅ Email Sender + Templates + Background Queue (NEW)
            // =========================================================
            services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
            services.Configure<BackgroundTaskOptions>(configuration.GetSection("BackgroundTasks"));
            services.Configure<EmailTemplateOptions>(configuration.GetSection("EmailTemplates"));

            // Queue (Channel) - Singleton
            services.AddSingleton<IBackgroundTaskQueue>(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<BackgroundTaskOptions>>().Value;
                return new ChannelBackgroundTaskQueue(opt.Capacity);
            });

            // Hosted background worker
            services.AddHostedService<QueuedHostedService>();


            // Email sender + template renderer
            services.AddTransient<IEmailSender, MailKitEmailSender>();
            services.AddSingleton<IEmailTemplateRenderer, FileEmailTemplateRenderer>();

            services.AddScoped<IEmailService, EmailService>();
            // Bound from either spelling.
            //
            // The class says the section is "ContractTemplates"; appsettings calls
            // it "ContractTemplateOptions". Neither is wrong on its own and
            // together they meant the section never bound at all, so ProviderBlock
            // silently fell back to the hard-coded default — which is the company
            // name that gets merged into every prepared contract.
            services.Configure<ContractTemplateOptions>(
                configuration.GetSection(ContractTemplateOptions.SectionName));
            services.Configure<ContractTemplateOptions>(
                configuration.GetSection("ContractTemplateOptions"));

            services.AddScoped<IContractDocumentGenerator, ContractDocumentGenerator>();
            services.AddScoped<IContractDraftService, ContractDraftService>();
            
            services.AddSingleton<IPdfGenerator, PlaywrightPdfGenerator>();
            services.AddSingleton<PlaywrightBrowserInstaller>();
            services.AddScoped<ContractCreationService>();
            services.AddScoped<IInvoiceNotificationService, InvoiceNotificationService>();
            services.AddScoped<QuotePublicLinkService>();
            return services;
        }
    }
}
