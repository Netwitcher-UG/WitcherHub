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

await using (var scope = app.Services.CreateAsyncScope())
{
    var playwrightBrowserInstaller = scope.ServiceProvider.GetRequiredService<PlaywrightBrowserInstaller>();
    await playwrightBrowserInstaller.EnsureInstalledAsync();
}
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("de") };

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});
await app.SeedAsync();
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

app.MapStaticAssets();
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
