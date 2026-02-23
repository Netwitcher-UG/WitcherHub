using Microsoft.AspNetCore.Localization;
using Serilog;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using WitcherHub.Configuration.Extensions;

var builder = WebApplication.CreateBuilder(args);
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
builder.Services.AddAppExceptionHandling();
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)  
        .ReadFrom.Services(services)                    
        .Enrich.FromLogContext()  
        .WriteTo.Console();
});
builder.Services.AddPresentation(builder.Configuration);

    
var app = builder.Build();
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

app.Run();
