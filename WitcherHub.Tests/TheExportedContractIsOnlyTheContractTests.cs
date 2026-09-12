namespace WitcherHub.Tests
{
    /// <summary>
    /// A downloaded contract carries the contract and nothing else.
    ///
    /// Exporting one produced a PDF with the browser's own furniture printed
    /// into every sheet:
    ///
    ///     9/9/26, 1:16 AM                Contract Signature · WitcherHub
    ///     …
    ///     https://localhost:7271/Contracts/Details/c0b2840c-…?version=1
    ///
    /// The date the reader happened to press the button, the title of a browser
    /// tab, and the internal URL of an administrative page — record id and query
    /// string included — stamped across a legal document that was going to a
    /// customer.
    ///
    /// None of it came from the application. The export was the browser printing
    /// the page, and a printing browser draws those two lines itself, in the page
    /// margin box, where no stylesheet of ours reaches. A site cannot switch them
    /// off: only the person at the keyboard can, in a checkbox inside the print
    /// dialog that most people never open. Zeroing the page margin does hide
    /// them, and takes the top and bottom margin of every continuation sheet with
    /// it, which on a multi-page contract is a worse document than the one we
    /// started with.
    ///
    /// So the export is rendered on the server instead, by the generator the
    /// signed copy already goes through — which sends an empty header template
    /// and a footer holding nothing but the page count. Verified against a
    /// two-page render: the pages read "Agenturvertrag / Seite eins / Seite 1 von
    /// 2" and "Seite zwei / Seite 2 von 2", with no date, no tab title and no URL
    /// anywhere in them.
    ///
    /// These tests read the page's source. The behaviour they guard is a wiring
    /// decision — which button goes where — and that is visible there and is
    /// exactly what regressed.
    /// </summary>
    public class TheExportedContractIsOnlyTheContractTests
    {
        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        private static string Source(params string[] parts)
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(new[] { root!.FullName }.Concat(parts).ToArray()));
        }

        private static string DetailsView() =>
            Source("WitcherHub", "Pages", "Contracts", "Details.cshtml");

        private static string DetailsPage() =>
            Source("WitcherHub", "Pages", "Contracts", "Details.cshtml.cs");

        private static string PdfGenerator() =>
            Source("WitcherHub.Infrastructure", "Services", "Pdf", "PlaywrightPdfGenerator.cs");

        // ============================================ the export is ours to render

        [Fact]
        public void TheContractCanBeDownloadedAsAPdfWithoutBeingSignedFirst()
        {
            // There was a server-rendered PDF already, and it was reachable only
            // on a signed contract. Every other contract's only export was the
            // browser's print dialog, which is where the header and the URL came
            // from.
            Assert.Contains("public async Task<IActionResult> OnGetPdfAsync", DetailsPage());
        }

        /// <summary>
        /// The <c>actions.Add(new PageAction { … })</c> block that mentions the
        /// given text, brace-matched so a neighbouring action cannot be read as
        /// part of it.
        /// </summary>
        private static string ActionDeclaring(string marker)
        {
            var view = DetailsView();
            var at = view.IndexOf(marker, StringComparison.Ordinal);

            Assert.True(at >= 0, $"No page action mentions “{marker}”.");

            var start = view.LastIndexOf("actions.Add", at, StringComparison.Ordinal);
            Assert.True(start >= 0);

            var depth = 0;

            for (var i = start; i < view.Length; i++)
            {
                if (view[i] == '{') depth++;
                else if (view[i] == '}' && --depth == 0) return view[start..(i + 1)];
            }

            throw new Xunit.Sdk.XunitException("The page action block is not closed.");
        }

        [Fact]
        public void TheDownloadButtonAsksTheServerRatherThanTheBrowser()
        {
            var download = ActionDeclaring("\"handler\"] = \"Pdf\"");

            // It is the primary action, so the obvious way to get a PDF is the
            // one that produces a clean one, and printing is the secondary.
            Assert.Contains("PageActionStyle.Primary", download);
            Assert.DoesNotContain("PageActionStyle.Primary", ActionDeclaring("btnPrint"));
        }

        [Fact]
        public void PrintingIsStillOfferedForPaper()
        {
            // Removing the artifacts is not a reason to remove printing. A
            // browser prints; it just should not be how a PDF is produced.
            var view = DetailsView();

            Assert.Contains("btnPrint", view);
            Assert.Contains("window.print()", view);
        }

        [Fact]
        public void WhatDownloadsIsTheVersionBeingRead()
        {
            // The URL in the report carried ?version=1: the reader was previewing
            // a version. A download that ignored it would hand over the approved
            // wording instead — two different documents behind one button, with
            // nothing on the file to say which.
            Assert.Contains("[\"version\"] = Model.Version?.ToString()", DetailsView());

            var page = DetailsPage();

            Assert.Contains("contract.Terms = draft.DocumentMarkdown;", page);
            Assert.Contains("-v{v}.pdf", page);
        }

        // ======================================= nothing of ours fills the margins

        [Fact]
        public void TheRendererSendsAnEmptyHeaderAndACountedFooter()
        {
            var generator = PdfGenerator();

            // Chromium draws its own date, title and URL only when it is left to
            // choose the templates. Supplying both replaces them outright, which
            // is why the signed PDF never had this problem.
            Assert.Contains("DisplayHeaderFooter = true", generator);
            Assert.Contains("HeaderTemplate = \"<div></div>\"", generator);

            Assert.Contains("pageNumber", generator);
            Assert.Contains("totalPages", generator);
        }

        [Fact]
        public void TheRendererNeverPutsALocationInTheFooter()
        {
            var generator = PdfGenerator();

            // The three things the browser would have written there.
            Assert.DoesNotContain("class=\"url\"", generator);
            Assert.DoesNotContain("class=\"title\"", generator);
            Assert.DoesNotContain("class=\"date\"", generator);
        }

        [Fact]
        public void TheServerNamesTheFileAfterTheContract()
        {
            // "Details.pdf", or the page title, would carry the same information
            // the header used to and be just as wrong on a customer's desk.
            Assert.Contains("$\"{contract.ContractNo}.pdf\"", DetailsPage());
        }

        // ============================================== and it fails out loud

        [Fact]
        public void AContractWithNoWordingSaysSoInsteadOfDownloadingAnEmptySheet()
        {
            var page = DetailsPage();

            Assert.Contains("has no wording yet", page);

            // The reason reaches the reader on the page they came from. A bare
            // status code leaves the browser showing its own error screen with
            // ours nowhere on it — the same reasoning the signed download uses.
            Assert.Contains("private IActionResult PdfUnavailable", page);
            Assert.Contains("TempData[\"Toast.Type\"] = \"error\";", page);
        }

        [Fact]
        public void TheRenderersOwnWordsGoToTheLogAndNotToTheScreen()
        {
            var page = DetailsPage();
            var handler = page[page.IndexOf("OnGetPdfAsync", StringComparison.Ordinal)..];

            // A Playwright stack trace on a contract page tells the reader
            // nothing and tells everyone else rather a lot.
            Assert.Contains("Reference {reference}", handler);
            Assert.Contains("_logger.LogError(ex", handler);
        }
    }
}
