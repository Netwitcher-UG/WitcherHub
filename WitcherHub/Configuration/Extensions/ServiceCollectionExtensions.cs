using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using WitcherHub.Application;
using WitcherHub.Configuration.Authorization;
using WitcherHub.Configuration.Filters;
using WitcherHub.Configuration.HealthChecks;
using WitcherHub.Configuration.Http;
using WitcherHub.Configuration.ModelBinding;
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

            // Railway (like every managed platform) terminates TLS at its edge and
            // forwards plain HTTP to the container. Without this the request looks
            // like http:// to the application, and three things quietly go wrong:
            // the sign-in cookie is written without its Secure flag because
            // Request.IsHttps is false, every absolute URL built from
            // Request.Scheme goes into an email as http://, and the logo the PDF
            // renderer fetches over http is redirected rather than served.
            //
            // The proxy is not on a known network and its address is not stable,
            // so the default KnownProxies/KnownNetworks check has to be cleared —
            // that check is what makes the middleware ignore the headers entirely
            // behind an unknown proxy. Only the two headers actually needed are
            // honoured, and the host is deliberately not among them: AllowedHosts
            // stays the thing that decides which host is acceptable.
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            services.AddRazorPages()
                .AddMvcOptions(options =>
                {
                    // A stale antiforgery token becomes a retry, not a bare 400.
                    options.Filters.Add<AntiforgeryFailureResultFilter>();

                    // Ahead of the built-in simple-type binder, so German decimal
                    // input ("0,00") is accepted regardless of request culture.
                    options.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());
                })
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

            // Secure by default: every Razor Page and controller requires an
            // authenticated user unless it opts out with [AllowAnonymous].
            // Without this fallback the Clients, Projects, Services and Project
            // Workspace pages were reachable without logging in.
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

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
                        // A rejected token used to fail silently: the browser was sent
                        // back to the login page with nothing written anywhere saying
                        // why, which made a signing or issuer mismatch look like the
                        // password being wrong.
                        OnAuthenticationFailed = context =>
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("WitcherHub.Authentication");

                            logger.LogWarning(
                                context.Exception,
                                "Rejected the access token for {Path}. The session will be treated as signed out.",
                                context.Request.Path);

                            return Task.CompletedTask;
                        },

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

                        // A session lapses after Jwt__AccessTokenMinutes whether or
                        // not the user is still working, and the page they are on
                        // has no idea. What happens next depends entirely on who
                        // is asking.
                        OnChallenge = context =>
                        {
                            if (context.Request.Path.StartsWithSegments("/api") ||
                                context.Request.Path.StartsWithSegments("/swagger"))
                            {
                                return Task.CompletedTask;
                            }

                            context.HandleResponse();

                            // Script gets an answer it can read. Redirecting it
                            // instead is what produced "the server returned an
                            // unreadable response" on the contract builder: fetch
                            // follows the redirect itself and hands the page's
                            // JavaScript a login page to parse as JSON.
                            if (RequestFormat.WantsJson(context.HttpContext))
                                return WriteSignedOutAsync(context.HttpContext, StatusCodes.Status401Unauthorized);

                            var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                            context.Response.Redirect($"/Auth/Login?returnUrl={returnUrl}");

                            return Task.CompletedTask;
                        },

                        OnForbidden = context =>
                        {
                            if (context.Request.Path.StartsWithSegments("/api") ||
                                context.Request.Path.StartsWithSegments("/swagger"))
                            {
                                return Task.CompletedTask;
                            }

                            if (RequestFormat.WantsJson(context.HttpContext))
                                return WriteSignedOutAsync(context.HttpContext, StatusCodes.Status403Forbidden);

                            var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                            context.Response.Redirect($"/Auth/Login?returnUrl={returnUrl}");

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

        /// <summary>
        /// Tells script, in a form it can read, that the session is over.
        ///
        /// The shape matches every other JSON reply the pages handle — an
        /// <c>ok</c> flag and a <c>message</c> — so no caller needs a special
        /// case to show it. <c>sessionExpired</c> lets the page offer the one
        /// action that actually helps, which is signing in again; the work on
        /// screen is still in the browser and is not lost by reloading after
        /// that. Nothing here says whether the account exists or what it may do.
        /// </summary>
        private static Task WriteSignedOutAsync(HttpContext context, int statusCode)
        {
            var signedOut = statusCode == StatusCodes.Status401Unauthorized;

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";

            var returnUrl = context.Request.Path + context.Request.QueryString;

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = false,
                sessionExpired = signedOut,
                transient = false,
                signInUrl = $"/Auth/Login?returnUrl={Uri.EscapeDataString(returnUrl)}",
                message = signedOut
                    ? "Your session has ended, so this could not be saved or sent. Sign in again and retry — " +
                      "nothing on this screen has been lost."
                    : "This account is not allowed to do that."
            });

            return context.Response.WriteAsync(payload);
        }
    }
}
