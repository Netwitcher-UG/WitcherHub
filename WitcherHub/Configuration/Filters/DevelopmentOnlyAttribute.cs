using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WitcherHub.Configuration.Filters
{
    /// <summary>
    /// Hides an endpoint outside the Development environment by returning 404.
    ///
    /// Used for the diagnostic controllers, which call Lexware (including customer
    /// delete), the OpenAI API and the mail sender. Those must never be reachable
    /// on a deployed environment.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var env = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

            if (!env.IsDevelopment())
                context.Result = new NotFoundResult();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
