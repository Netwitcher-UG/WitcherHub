using Serilog;
using System.Text.Json.Serialization;
using WitcherHub.Configuration.Extensions;

var builder = WebApplication.CreateBuilder(args);
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
builder.Services
    .AddRazorPages()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
var app = builder.Build();
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