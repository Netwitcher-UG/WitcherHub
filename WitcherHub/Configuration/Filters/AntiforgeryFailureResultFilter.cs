using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WitcherHub.Configuration.Filters
{
    /// <summary>
    /// Turns an antiforgery failure into a page the visitor can recover from.
    ///
    /// A stale token otherwise produces a bare "HTTP ERROR 400" with no
    /// explanation, which reads as the site being broken. It happens routinely: a
    /// page left open across a deploy, a form restored by the browser, a tab open
    /// overnight.
    ///
    /// This runs as a result filter rather than middleware because the framework's
    /// own antiforgery filter catches the exception and returns
    /// <see cref="IAntiforgeryValidationFailedResult"/> — nothing ever reaches the
    /// middleware pipeline to catch.
    /// </summary>
    public sealed class AntiforgeryFailureResultFilter : IAlwaysRunResultFilter
    {
        private readonly ILogger<AntiforgeryFailureResultFilter> _logger;

        public AntiforgeryFailureResultFilter(ILogger<AntiforgeryFailureResultFilter> logger)
            => _logger = logger;

        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not IAntiforgeryValidationFailedResult)
                return;

            var request = context.HttpContext.Request;

            _logger.LogWarning(
                "Rejected a request to {Path} because its antiforgery token could not be validated. " +
                "Expected for a page loaded before a deploy; the visitor is being sent back to retry.",
                request.Path);

            // Reload the same page so a fresh token is issued and the visitor can
            // simply submit again — not a redirect to login, which would be wrong
            // for someone already signed in on another form.
            var query = request.Query
                .Where(q => q.Key != "expired")
                .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}")
                .Append("expired=true");

            var path = request.Path.HasValue ? request.Path.Value! : "/";

            context.Result = new RedirectResult($"{path}?{string.Join("&", query)}");
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}
