using System.Text.RegularExpressions;
using WitcherHub.Infrastructure.Services.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests
{
    /// <summary>
    /// What a customer signed, and getting a copy of it back.
    ///
    /// Two things were wrong once a contract was signed.
    ///
    /// The signed PDF existed for about a second. The signing flow builds it,
    /// attaches it to the confirmation e-mail and lets it go; nothing stores it
    /// and nothing offered it afterwards, so the customer had a copy of the
    /// signed contract and we did not. The quote has had a Signed PDF button for
    /// as long as it has had signing.
    ///
    /// And the contract stayed editable. The builder page carried an IsLocked
    /// property and used it to grey the header fields, but the positions are
    /// rendered by script that never read it: move, duplicate and delete were
    /// live on a signed agreement, every field was typeable, "Paste existing
    /// text" was one click away, and none of the fetch handlers behind them
    /// asked what the contract's status was before writing. Rearranging a signed
    /// contract's positions was a working feature.
    /// </summary>
    public class ASignedContractStaysSignedTests
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

        private static string PositionsPage() =>
            Source("WitcherHub", "Pages", "Contracts", "Positions.cshtml.cs");

        private static string PositionsView() =>
            Source("WitcherHub", "Pages", "Contracts", "Positions.cshtml");

        private static string DetailsPage() =>
            Source("WitcherHub", "Pages", "Contracts", "Details.cshtml.cs");

        private static string DetailsView() =>
            Source("WitcherHub", "Pages", "Contracts", "Details.cshtml");

        private static string Builder() =>
            Source("WitcherHub", "wwwroot", "js", "pages", "contracts", "positions-builder.js");

        /// <summary>
        /// The body of a method, brace-matched from its declaration so a nested
        /// block cannot cut it short.
        ///
        /// Occurrences that are calls rather than declarations are skipped. The
        /// first version of this took the first match and the first brace after
        /// it, which on <c>await RefuseIfLockedJsonAsync(ct) is { } locked</c>
        /// read the pattern's own braces as the method's body — a helper that
        /// answers confidently about the wrong text.
        /// </summary>
        private static string MethodBody(string source, string nameFragment)
        {
            var from = 0;

            while (true)
            {
                var at = source.IndexOf(nameFragment, from, StringComparison.Ordinal);
                if (at < 0) return "";

                from = at + nameFragment.Length;

                var paren = source.IndexOf('(', at);
                if (paren < 0) return "";

                // Past the parameter list, which can itself contain brackets.
                var depth = 1;
                var i = paren + 1;

                while (i < source.Length && depth > 0)
                {
                    if (source[i] == '(') depth++;
                    else if (source[i] == ')') depth--;
                    i++;
                }

                while (i < source.Length && char.IsWhiteSpace(source[i])) i++;

                // An expression-bodied member: everything to the semicolon.
                if (i + 1 < source.Length && source[i] == '=' && source[i + 1] == '>')
                {
                    var end = source.IndexOf(';', i);
                    return end < 0 ? source[(i + 2)..] : source[(i + 2)..end];
                }

                // Otherwise a declaration opens a block here. Anything else is a call.
                if (i >= source.Length || source[i] != '{') continue;

                depth = 1;
                var open = i;
                i = open + 1;

                while (i < source.Length && depth > 0)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}') depth--;
                    i++;
                }

                return source[(open + 1)..(i - 1)];
            }
        }

        // ===================================================== the rule itself

        [Fact]
        public void SignedAndTerminatedAreLockedAndNothingElseIs()
        {
            Assert.True(ContractLock.IsLocked(DocumentStatus.Signed));
            Assert.True(ContractLock.IsLocked(DocumentStatus.Terminated));

            // A contract that is still being written, or was sent and not signed,
            // or was cancelled before anyone agreed to it, is not a record of an
            // agreement and editing it takes nothing away.
            Assert.False(ContractLock.IsLocked(DocumentStatus.Draft));
            Assert.False(ContractLock.IsLocked(DocumentStatus.Sent));
            Assert.False(ContractLock.IsLocked(DocumentStatus.Cancelled));
        }

        [Fact]
        public void TheReasonNamesWhichOfTheTwoItIs()
        {
            // "This contract is locked" tells the reader nothing they can act on.
            Assert.Contains("signed", ContractLock.Reason(DocumentStatus.Signed));
            Assert.Contains("terminated", ContractLock.Reason(DocumentStatus.Terminated));
            Assert.NotEqual(
                ContractLock.Reason(DocumentStatus.Signed),
                ContractLock.Reason(DocumentStatus.Terminated));
        }

        [Fact]
        public void BothPagesAskTheSameRuleRatherThanRestatingIt()
        {
            // The builder used to carry its own copy — "Status is Signed or
            // Terminated" written out beside the view. A second copy is a second
            // thing to remember when a third locked status appears.
            foreach (var page in new[] { PositionsPage(), DetailsPage() })
                Assert.Contains("ContractLock.IsLocked", page);

            Assert.DoesNotContain(
                "Contract?.Status is DocumentStatus.Signed or DocumentStatus.Terminated",
                PositionsPage());
        }

        // ============================================= the server refuses first

        [Fact]
        public void EveryHandlerThatChangesTheContractAsksTheLockFirst()
        {
            var page = PositionsPage();

            // Everything on the builder that writes. Reading handlers — totals,
            // job status, analysis progress — are deliberately not here: they
            // change nothing, and refusing them would stop a signed contract
            // being looked at.
            foreach (var handler in new[]
                     {
                         "OnPostBasicsAsync",
                         "OnPostImportTextAsync",
                         "OnPostConfirmExtractionAsync",
                         "OnPostSaveAsync",
                         "OnPostOrganizeAsync",
                         "OnPostGenerateDraftAsync",
                         "OnPostSaveDraftAsync",
                         "OnPostApproveDraftAsync"
                     })
            {
                var body = MethodBody(page, handler);

                Assert.False(string.IsNullOrWhiteSpace(body), $"{handler} is gone");
                Assert.True(
                    body.Contains("RefuseIfLockedJsonAsync") || body.Contains("RefuseIfLockedPageAsync"),
                    $"{handler} writes to the contract without asking whether it is locked. "
                    + "A disabled button is not a control — the request can be sent from a tab "
                    + "left open since before the customer signed.");
            }
        }

        [Fact]
        public void TheRefusalIsAskedBeforeAnythingIsWritten()
        {
            var page = PositionsPage();

            // A guard placed after the write is not a guard. For each handler the
            // refusal has to come before the first call into a service.
            foreach (var (handler, write) in new[]
                     {
                         ("OnPostSaveAsync", "_positions.SavePositionsAsync"),
                         ("OnPostImportTextAsync", "_drafts.ImportTextAsync"),
                         ("OnPostSaveDraftAsync", "_drafts.SaveEditedAsync"),
                         ("OnPostApproveDraftAsync", "_drafts.ApproveAsync"),
                         ("OnPostBasicsAsync", "_contracts.UpdateHeaderAsync"),
                         ("OnPostConfirmExtractionAsync", "_drafts.ConfirmExtractionAsync"),
                         ("OnPostGenerateDraftAsync", "_jobs.StartAsync"),
                         ("OnPostOrganizeAsync", "_jobs.StartAsync")
                     })
            {
                var body = MethodBody(page, handler);

                var guard = body.IndexOf("RefuseIfLocked", StringComparison.Ordinal);
                var writes = body.IndexOf(write, StringComparison.Ordinal);

                Assert.True(guard >= 0, $"{handler} has no guard");
                Assert.True(writes >= 0, $"{handler} no longer calls {write}");
                Assert.True(guard < writes,
                    $"{handler} calls {write} before asking whether the contract is locked");
            }
        }

        [Fact]
        public void AFetchIsRefusedAsJsonAndAFormPostAsAPage()
        {
            var page = PositionsPage();

            // Answering a fetch with a redirect hands the script a login page and
            // a parse error; answering a form post with JSON puts raw braces on
            // the screen. Both refusals say the same thing in the caller's own
            // shape.
            Assert.Matches(
                @"RefuseIfLockedJsonAsync[^}]*?Status409Conflict",
                Regex.Replace(page, @"\s+", " "));

            Assert.Contains("ContractLock.Reason", MethodBody(page, "RefuseIfLockedJsonAsync"));
            Assert.Contains("Toast.Message", MethodBody(page, "RefuseIfLockedPageAsync"));
            Assert.Contains("RedirectToPage", MethodBody(page, "RefuseIfLockedPageAsync"));
        }

        [Fact]
        public void AContractThatDoesNotExistIsNotReportedAsLocked()
        {
            // The guard runs before the handler's own not-found check, so it has
            // to hand a missing contract back rather than answering "this has been
            // signed" about something that is not there.
            var body = MethodBody(PositionsPage(), "LockedStatusAsync");

            Assert.False(string.IsNullOrWhiteSpace(body));
            Assert.Contains("if (contract is null) return null;", body);
        }

        // ============================================== and the page shows it

        [Fact]
        public void ThePositionsRowActionsAreDisabledOnASignedContract()
        {
            var js = Builder();

            // move up, move down, duplicate and delete are drawn by script from
            // the positions array. Nothing here read the page's own data-locked,
            // so all four were live on a signed contract.
            Assert.Contains("app.dataset.locked", js);
            Assert.Matches(@"if \(locked\) seal\(", js);

            var seal = MethodBody(js, "function seal(");
            Assert.False(string.IsNullOrWhiteSpace(seal), "there is no rule sealing a rendered position");

            Assert.Contains("select, button", seal);
            Assert.Contains("disabled = true", seal);
        }

        [Fact]
        public void ASignedContractsFieldsCanStillBeReadWhileNotBeingEditable()
        {
            var seal = MethodBody(Builder(), "function seal(");

            // readonly rather than disabled on the text: a disabled field is
            // greyed to the point of being hard to read, and reading it is the
            // whole reason to open a signed contract.
            Assert.Contains("readOnly = true", seal);

            // The controls readonly does not apply to have to be disabled instead.
            foreach (var kind in new[] { "checkbox", "date" })
                Assert.Contains(kind, seal);
        }

        [Fact]
        public void TheRowActionHandlerRefusesEvenIfADisabledButtonIsSomehowPressed()
        {
            var js = Builder();

            var at = js.IndexOf("listEl.addEventListener(\"click\"", StringComparison.Ordinal);
            Assert.True(at >= 0, "the row action handler is gone");

            var body = js[at..Math.Min(js.Length, at + 900)];

            var guard = body.IndexOf("if (locked) return;", StringComparison.Ordinal);
            var splice = body.IndexOf("positions.splice", StringComparison.Ordinal);

            Assert.True(guard >= 0, "delete and move rewrite the array with no lock check at all");
            Assert.True(splice < 0 || guard < splice, "the array is changed before the lock is asked");
        }

        [Fact]
        public void PasteExistingTextAndTheCustomerLinkAreClosedOffToo()
        {
            var view = PositionsView();

            // Both were named in the report. Pasting text stores a new version of
            // the wording; the customer link goes to where the address and the
            // representative — the parties the signed document names — are edited.
            Assert.Matches(
                @"data-action=""toggle-paste""[^>]*disabled=""@Model\.IsLocked""",
                Regex.Replace(view, @"\s+", " "));

            Assert.Matches(
                @"@if \(Model\.IsLocked\)\s*\{\s*<span[^>]*>Check address and representative</span>",
                Regex.Replace(view, @"\s+", " "));
        }

        [Fact]
        public void TheReasonReachesTheBrowserRatherThanBeingWrittenTwice()
        {
            // The script needs the sentence for its tooltips. Hard-coding it there
            // would give a terminated contract the signed contract's wording.
            Assert.Contains("data-lock-reason=\"@Model.LockReason\"", PositionsView());
            Assert.Contains("app.dataset.lockReason", Builder());
        }

        [Fact]
        public void AnUnlockedPageDoesNotCarryASentenceSayingItIsLocked()
        {
            foreach (var page in new[] { PositionsPage(), DetailsPage() })
                Assert.Contains("IsLocked ? ContractLock.Reason", page);
        }

        [Fact]
        public void EditingTheContractHeaderIsClosedOffFromItsOwnPageToo()
        {
            // Contracts/Edit is a second way into the same record. It refuses
            // anything that is not Draft, which is stricter than this lock and
            // covers it — but only for as long as those checks are there.
            var edit = Source("WitcherHub", "Pages", "Contracts", "Edit.cshtml.cs");

            foreach (var handler in new[]
                     {
                         "OnPostUpdateHeaderAsync",
                         "OnPostAddItemAsync",
                         "OnPostUpdateItemAsync",
                         "OnPostDeleteItemAsync"
                     })
            {
                var body = MethodBody(edit, handler);

                Assert.False(string.IsNullOrWhiteSpace(body), $"{handler} is gone");
                Assert.Contains("DocumentStatus.Draft", body);
            }
        }

        // ================================================ getting the copy back

        [Fact]
        public void ThereIsAHandlerThatHandsBackTheSignedContract()
        {
            var body = MethodBody(DetailsPage(), "OnGetSignedPdfAsync");

            Assert.False(string.IsNullOrWhiteSpace(body), "there is no signed PDF handler");

            // Rebuilt from the same document and the same stored signature the
            // confirmation e-mail used, because nothing stored the file itself.
            Assert.Contains("ContractPdfDocument.Build", body);
            Assert.Contains("ContractPdfHtmlBuilder.BuildSigned", body);
            Assert.Contains("application/pdf", body);
            Assert.Contains("-signed.pdf", body);
        }

        [Fact]
        public void TheHandlerChecksTheStatusItself()
        {
            var body = MethodBody(DetailsPage(), "OnGetSignedPdfAsync");

            // The button is only drawn on a signed contract, and the URL can be
            // typed on any of them.
            Assert.Contains("DocumentStatus.Signed", body);
            Assert.Contains("SignedPdfUnavailable", body);
        }

        [Fact]
        public void TheButtonIsOnlyDrawnWhenPressingItWillProduceSomething()
        {
            var view = DetailsView();
            var flat = Regex.Replace(view, @"\s+", " ");

            Assert.Matches(@"if \(Model\.CanDownloadSignedPdf\)[^}]*Label = ""Signed PDF""", flat);

            // Stricter than the signed badge, which also reports a signature found
            // on a contract whose status says otherwise. A button that answers
            // with an apology is worse than no button.
            var can = Regex.Match(
                DetailsPage(),
                @"CanDownloadSignedPdf =(?<expr>.*?);",
                RegexOptions.Singleline);

            Assert.True(can.Success, "nothing decides whether the download is offered");
            Assert.Contains("DocumentStatus.Signed", can.Groups["expr"].Value);
            Assert.Contains("SignatureImageOf", can.Groups["expr"].Value);
        }

        [Fact]
        public void TheViewAndTheHandlerAskTheSameTwoQuestions()
        {
            var page = DetailsPage();

            // Asked through shared helpers rather than written out twice, so the
            // button and the answer to pressing it cannot drift apart.
            foreach (var helper in new[] { "SignedSignature", "SignatureImageOf" })
            {
                Assert.True(
                    Regex.Matches(page, Regex.Escape(helper) + @"\(").Count >= 3,
                    $"{helper} is not used by both the view's decision and the handler's");
            }
        }

        [Fact]
        public void ASignatureRowWithNoDrawnImageIsNotOfferedAsADownload()
        {
            // A signing link that was issued and never used leaves a row behind
            // with no SignedAt; a row can also carry no image. A contract PDF
            // headed "signed" with an empty line under it is worse than none.
            var body = MethodBody(DetailsPage(), "SignatureImageOf");

            Assert.False(string.IsNullOrWhiteSpace(body));
            Assert.Contains("dataUrl", body);
            Assert.Contains("IsNullOrWhiteSpace", body);

            var signed = MethodBody(DetailsPage(), "SignedSignature");
            Assert.Contains("SignedAt is not null", signed);
        }

        [Fact]
        public void TheRenderersOwnWordsDoNotReachTheScreen()
        {
            var body = MethodBody(DetailsPage(), "OnGetSignedPdfAsync");

            // A failed render says why in the log, under a reference the reader
            // can quote. What it says is Chromium's, and it is not for the screen.
            Assert.Contains("_logger.LogError", body);
            Assert.Contains("Reference", body);
            Assert.DoesNotContain("ex.Message", body);
        }

        [Fact]
        public void TheDocumentIsBuiltInOnePlaceForBothPathsThatNeedIt()
        {
            // The signing page held ~470 lines of document building. Downloading
            // the signed copy needs exactly that, and a second copy of it would be
            // two documents that drift apart while both claiming to be the
            // contract that was signed.
            var sign = Source("WitcherHub", "Pages", "Contracts", "Sign.cshtml.cs");

            Assert.Contains("ContractPdfDocument.Build", sign);
            Assert.Contains("ContractPdfHtmlBuilder.BuildSigned", sign);

            // And the private copies are gone rather than left behind unused.
            Assert.DoesNotContain("private ContractPdfDocumentModel BuildContractPdfModel", sign);
            Assert.DoesNotContain("string BuildSignedContractPdfHtml", sign);
        }
    }
}
