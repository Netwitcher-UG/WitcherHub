using Ganss.Xss;
using Markdig;

namespace WitcherHub.Rendering
{
    /// <summary>
    /// The one way a contract's Markdown becomes HTML.
    ///
    /// This existed five times — on the contract page, the editor, the signing
    /// page, the demo signing page and the project workspace — each building its
    /// own pipeline and its own sanitiser. Five copies of a security-relevant
    /// decision that had already drifted apart.
    ///
    /// The drift mattered the moment contracts started carrying structural
    /// markup. A German contract's party block and signature block are HTML with
    /// classes the stylesheet hangs off, and <see cref="HtmlSanitizer"/> does
    /// <em>not</em> allow the class attribute by default: it kept the div and
    /// dropped the class, so the document rendered as unstyled prose while every
    /// other test passed. That was found by a test, not by looking at the page.
    ///
    /// So class is allowed — but only for the names the contract layout defines.
    /// A whitelist rather than a blanket permission, because the alternative
    /// lets any class name through on documents that can originate as text a
    /// customer pasted in.
    /// </summary>
    public static class ContractMarkdown
    {
        /// <summary>
        /// The structural classes a composed German contract uses. Anything else
        /// is stripped, including from supplied documents.
        /// </summary>
        private static readonly string[] LayoutClasses =
        [
            "vertrag-parteien",
            "vertrag-partei",
            "vertrag-rolle",
            "vertrag-konjunktion",
            "vertrag-laufzeit",
            "vertrag-absatz",
            "vertrag-buchstaben",
            "vertrag-unterschriften",
            "vertrag-unterschrift",
            "vertrag-unterschrift-linie",
            "vertrag-unterschrift-name",
            "vertrag-ort-datum"
        ];

        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        /// <summary>
        /// Renders contract Markdown to HTML that is safe to place on a page.
        /// </summary>
        public static string ToHtml(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";

            // Normalised first: a document that arrived with CRLF renders with
            // stray breaks otherwise, and supplied documents arrive however the
            // customer's machine wrote them.
            var html = Markdown.ToHtml(markdown.Replace("\r\n", "\n"), Pipeline);

            return Sanitiser().Sanitize(html);
        }

        /// <summary>
        /// A sanitiser configured the same way for every page that shows a
        /// contract. Built per call — HtmlSanitizer is not documented as
        /// thread-safe, and these are cheap next to the parse.
        /// </summary>
        private static HtmlSanitizer Sanitiser()
        {
            var sanitizer = new HtmlSanitizer();

            sanitizer.AllowedSchemes.Add("mailto");

            // Not allowed by default. Without it the layout hooks vanish and the
            // document renders as a wall of prose.
            sanitizer.AllowedAttributes.Add("class");

            // With AllowedClasses non-empty, only these survive — so allowing the
            // attribute does not become permission for arbitrary class names.
            foreach (var name in LayoutClasses)
                sanitizer.AllowedClasses.Add(name);

            return sanitizer;
        }
    }
}
