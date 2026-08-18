namespace WitcherHub.Configuration.Http
{
    /// <summary>
    /// Whether a request came from script expecting JSON, or from a browser
    /// expecting a page.
    ///
    /// This matters more than it looks. Everything on the contract builder —
    /// analysing a document, extracting positions, saving, generating — is a
    /// <c>fetch</c> POST that reads the reply with <c>response.json()</c>. Answer
    /// one of those with a redirect and the browser follows it *transparently*:
    /// script asks for JSON, gets 200 OK and a complete HTML login page, and
    /// throws on the first character. The user is shown "the server returned an
    /// unreadable response" and has no way to know they were simply signed out.
    ///
    /// So a redirect is only ever a correct answer to a request that can show a
    /// page. Every place that would otherwise redirect asks this first.
    /// </summary>
    public static class RequestFormat
    {
        /// <summary>
        /// True when the caller is script that will try to parse the reply.
        /// </summary>
        public static bool WantsJson(HttpContext context)
        {
            var request = context.Request;

            // The API and the pages' JSON handlers.
            if (request.Path.StartsWithSegments("/api")) return true;

            // Set by fetch/XHR wrappers, including ours.
            if (string.Equals(
                    request.Headers["X-Requested-With"],
                    "XMLHttpRequest",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A JSON body means a JSON caller: none of the HTML forms post one.
            if ((request.ContentType ?? "").Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            // Accept is checked last and most carefully. A browser navigating to a
            // page sends "text/html,...,*/*", which must not be read as wanting
            // JSON, so an explicit application/json only counts when it is not
            // trailing behind text/html.
            var accept = request.Headers.Accept.ToString();

            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) &&
                !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // fetch() sets this to "empty" for a plain request; a navigation sets
            // "document". This is the most reliable signal modern browsers give,
            // and it is what separates a JSON POST from a form post to the same
            // handler when neither carries a JSON content type.
            var dest = request.Headers["Sec-Fetch-Dest"].ToString();
            if (string.Equals(dest, "empty", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }
    }
}
