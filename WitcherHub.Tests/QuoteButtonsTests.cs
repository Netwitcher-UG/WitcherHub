namespace WitcherHub.Tests
{
    /// <summary>
    /// The PDF and Copy Link buttons on a quote.
    ///
    /// Both were reported as not working. The PDF one had a real defect: its
    /// handler is reached by <c>fetch</c>, not by navigation, and on failure it
    /// answered with a redirect. A redirect is transparent to fetch — the browser
    /// follows it all the way to a page, the script is handed 200 and HTML, and
    /// the only thing it could say was "Failed to download PDF." The reason went
    /// into TempData, and the swallowed request consumed it, so reloading the page
    /// did not show it either. Verified against the running application: a failing
    /// PDF request came back 200 text/html having followed a redirect to /Projects.
    ///
    /// Copy Link's server side was correct. Its client side threw the link away:
    /// the clipboard write comes after an await, and <c>navigator.clipboard</c> is
    /// absent outside a secure context and rejects when the document is not
    /// focused. That landed in a catch reporting "Server error" — wrong twice
    /// over, because the server had succeeded and issued a token, and the user was
    /// left with no link at all.
    /// </summary>
    public class QuoteButtonsTests
    {
        private static string Page() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Quotes", "Details.cshtml"));

        private static string PageModel() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Quotes", "Details.cshtml.cs"));

        // ===================================================== the PDF button

        [Fact]
        public void AFailedPdfIsRefusedInScriptsOwnTermsRatherThanRedirected()
        {
            var model = PageModel();

            var handler = Between(model, "public async Task<IActionResult> OnGetPdfAsync", "OnPostSendAsync");

            // The check that separates a fetch from a navigation already exists in
            // this application; the quote PDF handler was not using it.
            Assert.Contains("RequestFormat.WantsJson(HttpContext)", handler);
            Assert.Contains("StatusCodes.Status400BadRequest", handler);
            Assert.Contains("message = ex.Message", handler);
        }

        [Fact]
        public void APlainNavigationStillGetsThePageAndTheToast()
        {
            // The anchor is still an anchor: without script, following it must
            // land somewhere that can show why nothing was downloaded.
            var handler = Between(
                PageModel(), "public async Task<IActionResult> OnGetPdfAsync", "OnPostSendAsync");

            Assert.Contains("TempData[\"Toast.Message\"]", handler);
            Assert.Contains("RedirectToPage(\"./Details\"", handler);
        }

        [Fact]
        public void TheReasonThePdfFailedIsShownInsteadOfABlankAlert()
        {
            var page = Page();

            // Was: alert('Failed to download PDF.') — the same words for a refused
            // request, an expired session and a dropped connection.
            Assert.DoesNotContain("alert('Failed to download PDF.')", page);

            Assert.Contains("readQuoteFailure(res, 'The PDF could not be generated.')", page);
            Assert.Contains("showQuoteToast('error', 'PDF not created'", page);
        }

        [Fact]
        public void ARequestThatNeverCompletedFallsBackToTheLinkItself()
        {
            var page = Page();

            // The click is intercepted so the button can show a spinner and name
            // the file. When that path cannot run at all, the anchor still points
            // at the handler — using it beats telling the user nothing worked.
            Assert.Contains("window.location.href = url;", page);
        }

        // =============================================== the Copy Link button

        [Fact]
        public void AnIssuedLinkIsNeverLostBecauseTheClipboardRefused()
        {
            var page = Page();

            // By the time this runs the server has already created the token. The
            // clipboard failing is not a reason to leave the user with nothing.
            Assert.Contains("async function copyQuoteText(text)", page);
            Assert.Contains("document.execCommand('copy')", page);
            Assert.Contains("window.prompt('Copy the public quote link:'", page);

            // And the old wording, which blamed the server for a browser refusing
            // to write to the clipboard.
            Assert.DoesNotContain("'Failed to copy public link.'", page);
        }

        [Fact]
        public void TheClipboardApiIsOnlyUsedWhereItExists()
        {
            var page = Page();

            // navigator.clipboard is undefined outside a secure context; reading it
            // unguarded is how this became an exception rather than a fallback.
            Assert.Contains("navigator.clipboard && window.isSecureContext", page);
        }

        [Fact]
        public void AnExpiredSessionSaysSoRatherThanBlamingTheRequest()
        {
            var page = Page();

            // A session timeout answers a fetch with a redirect to the sign-in
            // page, which arrives as 200 and HTML. Both buttons now recognise it.
            Assert.Contains("res.redirected || contentType.includes('text/html')", page);
            Assert.Contains("Your session has ended", page);
        }

        [Fact]
        public void BothButtonsIdentifyThemselvesAsScript()
        {
            var page = Page();

            // So an expired session is answered with a 401 rather than with the
            // sign-in page in the first place — the mechanism this application
            // already uses everywhere else.
            Assert.Contains("'X-Requested-With': 'XMLHttpRequest'", page);

            // Declared once and carried by both requests: the link fetch spreads
            // it alongside its own headers, the PDF fetch passes it whole.
            Assert.Contains("const QUOTE_FETCH_HEADERS", page);
            Assert.Contains("...QUOTE_FETCH_HEADERS", page);
            Assert.Contains("headers: QUOTE_FETCH_HEADERS", page);
        }

        // ---------------------------------------------------------------

        private static string Between(string text, string from, string to)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return "";

            var end = text.IndexOf(to, start, StringComparison.Ordinal);
            return end < 0 ? text[start..] : text[start..end];
        }

        private static int Occurrences(string text, string needle)
        {
            var count = 0;
            var at = 0;

            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }
    }
}
