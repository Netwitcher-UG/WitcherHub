using Microsoft.AspNetCore.Http;
using WitcherHub.Configuration.Http;

namespace WitcherHub.Tests
{
    /// <summary>
    /// A request that will be parsed as JSON must never be answered with a
    /// redirect.
    ///
    /// This is the whole of the bug reported as "The server returned an
    /// unreadable response", which the owner read — reasonably — as the contract
    /// generation or the OpenAI account being broken. It was neither.
    ///
    /// The session token lasts Jwt__AccessTokenMinutes and does not slide, so
    /// after an hour on the contract builder the next action was challenged. The
    /// challenge handler redirected to the sign-in page. The browser's fetch
    /// follows redirects *itself* and transparently: the page's JavaScript asked
    /// for JSON and was handed 200 OK and a complete HTML login page, which threw
    /// on the first character. The two AI buttons are simply the ones you press
    /// after a long time reading a contract, which is why it looked like an AI
    /// fault.
    ///
    /// The same trap existed on the antiforgery filter, which redirected a stale
    /// token back to the page.
    ///
    /// So the rule that separates the two audiences is worth testing directly.
    /// </summary>
    public class JsonRequestNeverRedirectsTests
    {
        // -------------------------------------------------------------
        // Callers that will parse the answer
        // -------------------------------------------------------------

        [Fact]
        public void AJsonBodyMeansAJsonCaller()
        {
            // Exactly what the contract builder's post() sends.
            Assert.True(WantsJson(contentType: "application/json"));
        }

        [Fact]
        public void TheFetchWrapperHeaderIsEnough()
        {
            Assert.True(WantsJson(headers: ("X-Requested-With", "XMLHttpRequest")));
        }

        [Fact]
        public void FetchIsRecognisedByItsDestinationEvenWithoutAJsonBody()
        {
            // A form-encoded fetch — no JSON content type, no wrapper header.
            // Sec-Fetch-Dest is what still separates it from a navigation.
            Assert.True(WantsJson(
                contentType: "application/x-www-form-urlencoded",
                headers: ("Sec-Fetch-Dest", "empty")));
        }

        [Fact]
        public void TheApiAlwaysWantsJson()
        {
            Assert.True(WantsJson(path: "/api/contracts"));
        }

        [Fact]
        public void AnExplicitJsonAcceptCounts()
        {
            Assert.True(WantsJson(headers: ("Accept", "application/json")));
        }

        // -------------------------------------------------------------
        // Callers that must still be shown a page
        // -------------------------------------------------------------

        [Fact]
        public void ABrowserNavigationIsNotAJsonCaller()
        {
            // The header a browser actually sends when you click a link. It
            // contains "*/*", and an earlier version of this rule read that as
            // willingness to accept JSON — which would have turned every expired
            // session into a raw 401 instead of the sign-in page.
            Assert.False(WantsJson(
                headers: [
                    ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"),
                    ("Sec-Fetch-Dest", "document")
                ]));
        }

        [Fact]
        public void AnOrdinaryFormPostIsNotAJsonCaller()
        {
            // The Basics form on the same page posts like this and expects to be
            // redirected back to the page it came from.
            Assert.False(WantsJson(
                contentType: "application/x-www-form-urlencoded",
                headers: [
                    ("Accept", "text/html,application/xhtml+xml,*/*;q=0.8"),
                    ("Sec-Fetch-Dest", "document")
                ]));
        }

        [Fact]
        public void AJsonAcceptTrailingBehindHtmlIsNotEnough()
        {
            // Some browsers list application/json far down the Accept header on a
            // navigation. The page wins when both are named.
            Assert.False(WantsJson(headers: ("Accept", "text/html,application/json;q=0.1")));
        }

        [Fact]
        public void ABareRequestWithNoSignalsIsTreatedAsAPage()
        {
            // The safe default: showing a sign-in page to script is recoverable
            // once the page reports it, but answering a navigation with raw JSON
            // strands the visitor on a blank screen.
            Assert.False(WantsJson());
        }

        // -------------------------------------------------------------

        private static bool WantsJson(
            string path = "/Contracts/Positions/00000000-0000-0000-0000-000000000001",
            string? contentType = null,
            params (string Name, string Value)[] headers)
        {
            var context = new DefaultHttpContext();

            context.Request.Path = path;
            context.Request.Method = "POST";

            if (contentType is not null)
                context.Request.ContentType = contentType;

            foreach (var (name, value) in headers)
                context.Request.Headers[name] = value;

            return RequestFormat.WantsJson(context);
        }
    }
}
