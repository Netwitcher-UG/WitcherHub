using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Localization;
using Serilog;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using WitcherHub.Configuration.Extensions;
using WitcherHub.Infrastructure.Services.Pdf;

var builder = WebApplication.CreateBuilder(args);

// Fail fast with an actionable message when a required secret is missing,
// instead of surfacing a null reference later inside a request.
builder.Configuration.ValidateRequiredConfiguration();

builder.Logging.ClearProviders();

builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Logging.AddFilter("WitcherHub", LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
builder.Services.AddAppExceptionHandling();
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)  
        .ReadFrom.Services(services)                    
        .Enrich.FromLogContext();
});
builder.Services.AddPresentation(builder.Configuration);

    
var app = builder.Build();

app.LogConfigurationReport();

// PDF rendering needs a browser, and fetching one is not a reason to refuse to
// start. This used to be awaited here and to rethrow, which made a cold start
// wait on a ~150MB download before the port was ever opened, and turned any
// hiccup fetching it into a failed deploy — taking sign-in, contracts and
// invoices down over a feature most requests never touch. The image now carries
// the browser (see Dockerfile), so this is a no-op there; where it is not
// already present it is fetched in the background and only PDF generation waits
// for it.
_ = Task.Run(async () =>
{
    await using var scope = app.Services.CreateAsyncScope();

    var installer = scope.ServiceProvider.GetRequiredService<PlaywrightBrowserInstaller>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("WitcherHub.Startup");

    try
    {
        await installer.EnsureInstalledAsync();
    }
    catch (Exception ex)
    {
        // Said plainly rather than swallowed: PDF generation will fail until this
        // succeeds, and the reason belongs in the log at the moment it happened.
        logger.LogError(
            ex,
            "The PDF browser could not be prepared. The application is running and " +
            "everything except PDF generation works; PDF generation will retry on first use.");
    }
});
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("de") };

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});
// Schema first: seeding and Data Protection both read tables that a pending
// migration may not have created yet.
await app.MigrateDatabaseAsync();

await app.SeedAsync();
// First in the pipeline, before anything reads the scheme, the client address
// or builds a URL from them. Behind Railway's proxy the connection to this
// process is plain HTTP; these headers are what carry the fact that the client
// arrived over HTTPS. Placed ahead of the request log too, so the log records
// the scheme the user actually used.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseAppExceptionHandling();
    app.UseHsts();
}


app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

var enableSwagger = app.Environment.IsDevelopment()
                 || app.Environment.IsStaging()
                 || app.Configuration.GetValue<bool>("Swagger:Enabled");

if (enableSwagger)
{
    app.UseSwaggerDocumentation();
}

app.UseAuthentication();
app.UseAuthorization();

// Static assets must stay anonymous. MapStaticAssets registers an endpoint per
// asset, and the global fallback authorization policy would otherwise apply to
// them: the fingerprinted URLs the tag helpers emit (main.r2zts9daby.css) exist
// only as endpoints, never on disk, so UseStaticFiles cannot serve them and the
// browser got a redirect to the login page instead of every stylesheet.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapControllers();

// Liveness: the process is up. Readiness: PostgreSQL is reachable.
// Both are anonymous so the platform can probe them without a token.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();
