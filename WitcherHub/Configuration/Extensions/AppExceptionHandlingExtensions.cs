using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;

namespace WitcherHub.Configuration.Extensions
{
    public static class AppExceptionHandlingExtensions
    {
        public static IServiceCollection AddAppExceptionHandling(this IServiceCollection services)
            => services;

        public static IApplicationBuilder UseAppExceptionHandling(this IApplicationBuilder app)
        {
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var feature = context.Features.Get<IExceptionHandlerFeature>();
                    var ex = feature?.Error;
                    if (ex is null) return;

                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("GlobalExceptionHandler");

                    var (status, title, detail) = MapException(ex);

                    if (status >= 500)
                        logger.LogError(ex, "Unhandled exception. {Method} {Path}", context.Request.Method, context.Request.Path);
                    else
                        logger.LogWarning(ex, "Handled exception. {Status} {Method} {Path}", status, context.Request.Method, context.Request.Path);

                    context.Response.Clear();
                    context.Response.StatusCode = status;
                    context.Response.Headers.CacheControl = "no-store";

                    if (WantsJson(context))
                    {
                        context.Response.ContentType = "application/problem+json; charset=utf-8";

                        var problem = new ProblemDetails
                        {
                            Status = status,
                            Title = title,
                            Detail = detail,
                            Instance = context.Request.Path
                        };

                        // لو عندك ValidationAppException (اختياري)
                        if (ex is WitcherHub.Application.Common.Exceptions.ValidationAppException vex)
                            problem.Extensions["errors"] = vex.Errors;

                        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                        await context.Response.WriteAsync(json);
                        return;
                    }

                    context.Response.ContentType = "text/html; charset=utf-8";

                    var safeTitle = HtmlEnc(title);
                    var safeDetail = HtmlEnc(detail);

                    await context.Response.WriteAsync($$"""
<!doctype html>
<html lang="en" data-bs-theme="blue-theme">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>{{safeTitle}}</title>
  <link href="/assets/css/bootstrap.min.css" rel="stylesheet" />
</head>
<body class="p-4">
  <div class="container">
    <div class="card rounded-4">
      <div class="card-body">
        <h4 class="mb-2">{{safeTitle}}</h4>
        <p class="text-muted mb-3">{{safeDetail}}</p>
        <div class="d-flex gap-2">
          <a class="btn btn-primary" href="javascript:history.back()">Back</a>
          <a class="btn btn-outline-secondary" href="/">Home</a>
        </div>
      </div>
    </div>
  </div>
</body>
</html>
""");
                });
            });

            return app;
        }

        private static (int Status, string Title, string Detail) MapException(Exception ex)
        {
            // لو عندك AppException مخصص
            if (ex is WitcherHub.Application.Common.Exceptions.AppException aex)
                return (aex.StatusCode, aex.Title, aex.Message);

            if (ex is ArgumentException)
                return ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message);

            return ((int)HttpStatusCode.InternalServerError, "Server Error", "An unexpected error occurred.");
        }

        private static bool WantsJson(HttpContext ctx)
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) return true;

            if (string.Equals(ctx.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                return true;

            var accept = ctx.Request.Headers.Accept.ToString();
            if (!string.IsNullOrWhiteSpace(accept) &&
                accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            var ct = ctx.Request.ContentType ?? "";
            if (ct.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string HtmlEnc(string? s)
            => (s ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#039;");
    }
}
