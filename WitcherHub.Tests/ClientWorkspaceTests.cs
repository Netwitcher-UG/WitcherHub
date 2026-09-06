using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests
{
    /// <summary>
    /// Client Details as a workspace rather than a record viewer.
    ///
    /// What the page was: a client's name beside the word "NotExported"; a
    /// half-width card of basic information mostly reading "—"; beside it, given
    /// equal weight, seven raw integration identifiers also reading "—"; a
    /// Projects table saying "No projects." with nothing to do about it; and two
    /// unlabelled icons in the corner, one of which deleted the client.
    ///
    /// The commonest thing anyone wants from a client — to start a project for
    /// them — was not on the page at all.
    /// </summary>
    public class ClientWorkspaceTests
    {
        private static string Page() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Clients", "Details.cshtml"));

        private static string PageModel() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Clients", "Details.cshtml.cs"));

        private static string Script() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "wwwroot", "js", "pages", "clients", "clients.js"));

        // ============================================================ actions

        [Fact]
        public void CreatingAProjectIsOfferedOnTheClient()
        {
            var page = Page();

            // Twice on purpose: in the header for someone scanning the top, and
            // beside the Projects section for someone already down there.
            Assert.Equal(3, Occurrences(page, "data-vc-action=\"create-project\""));

            Assert.Contains("Create project", page);
        }

        [Fact]
        public void EveryHeaderActionSaysWhatItDoes()
        {
            var header = Between(Page(), "<!-- Top Summary -->", "<div class=\"collapse mt-4\"");

            // Two unlabelled icons — an amber upload arrow and a red bin — is what
            // this was. Each action now carries its own words.
            foreach (var label in new[] { "Create project", "Edit client", "Send to accounting", "Delete" })
                Assert.Contains(label, header);

            // And the icon-only buttons are gone.
            Assert.DoesNotContain("btn p-0 border-0 bg-transparent text-danger", header);
        }

        [Fact]
        public void DeletingIsTheLastActionAndAsksFirst()
        {
            var page = Page();

            // Outlined rather than filled, so it does not compete with the thing
            // people actually came to do.
            Assert.Contains("btn-outline-danger", page);

            // And it opens a confirmation rather than acting on the click.
            Assert.Contains("data-bs-target=\"#vc-deleteClientCollapse\"", page);
            Assert.Contains("This action cannot be undone.", page);
        }

        [Fact]
        public void EditingOpensTheEditorThatExists()
        {
            // There is no /Clients/Edit page — editing happens in place — so the
            // header button triggers that rather than linking somewhere that 404s.
            Assert.Contains("data-vc-action=\"edit-basic\"", Page());
            Assert.DoesNotContain("/Clients/Edit/", Script());
        }

        // ======================================================= the projects

        [Fact]
        public void AClientWithNoProjectsIsToldWhatToDo()
        {
            var page = Page();

            Assert.Contains("vc-projectsEmpty", page);
            Assert.Contains("No projects for this client yet", page);

            // The empty state carries the action: a client with no projects is
            // exactly when the section is most worth something.
            var empty = Between(page, "vc-projectsEmpty", "vc-projectsTableWrap");
            Assert.Contains("data-vc-action=\"create-project\"", empty);
        }

        [Fact]
        public void TheEmptyStateIsNotATableRow()
        {
            // "No projects." as a cell with three empty cells beside it. The
            // markup is what matters — the phrase still appears in the comments
            // explaining why it went.
            Assert.DoesNotContain("colspan=\"4\"", Page());
            Assert.DoesNotContain("colspan=\"4\"", Script());

            // The tbody is emptied rather than filled with an excuse.
            Assert.Contains("tbody.innerHTML = \"\";", Script());
        }

        [Fact]
        public void TheProjectListDoesNotForceHorizontalScrolling()
        {
            var page = Page();

            // A four-column colgroup of fixed percentages inside a card is what
            // produced a scrollbar on a list of three projects. That is still
            // banned, and it was always the actual cause.
            var section = Between(page, "<!-- 4) Projects -->", "</table>");

            Assert.DoesNotContain("<colgroup>", section);

            // This test also banned `table-responsive` outright, which went too
            // far: the wrapper does not create a scrollbar, it only offers one
            // when the content genuinely does not fit. Measured in a browser on
            // a client with real project titles:
            //
            //     1440  wrapper scrolls: false   page scrolls sideways: false
            //     1024  wrapper scrolls: false   page scrolls sideways: false
            //      768  wrapper scrolls: false   page scrolls sideways: false
            //      390  wrapper scrolls: true    page scrolls sideways: false
            //      360  wrapper scrolls: true    page scrolls sideways: false
            //
            // Without it the table was 361px wide on a 360px phone and took the
            // whole page sideways with it, cutting the Action column off the
            // screen. The wrapper is how the table keeps that to itself, and
            // ClientDetailsFitsTheScreenTests holds the phone case.
            Assert.Contains("table-responsive", section);
        }

        // ========================================================== integration

        [Fact]
        public void IntegrationInternalsDoNotDominateThePage()
        {
            var page = Page();

            // Still present — they matter when a sync goes wrong — but behind a
            // disclosure rather than in a card of their own beside the client's
            // actual details.
            foreach (var field in new[] { "vc-lx-contactId", "vc-lx-organizationId", "vc-lx-version" })
                Assert.Contains(field, page);

            Assert.Contains("<details", page);

            var details = Between(page, "<details", "</details>");
            Assert.Contains("vc-lx-contactId", details);

            // And the section is no longer titled with the vendor's name as though
            // it were one of the client's own attributes.
            Assert.DoesNotContain("<h5 class=\"mb-0 fw-bold\">Lexware</h5>", page);
        }

        [Fact]
        public void TheExportStateIsSaidInWordsNotEnumNames()
        {
            var script = Script();

            // "NotExported" was printed beside the customer's name, which reads as
            // an error and is not anybody's language.
            Assert.Contains("Not in accounting yet", script);
            Assert.Contains("In accounting", script);
            Assert.Contains("From accounting", script);

            // The states themselves are untouched: only their labels changed.
            Assert.Contains("NotExported", script);
        }

        // ============================================================== status

        [Fact]
        public void TheProjectStatusComesFromTheOneVocabulary()
        {
            var model = PageModel();

            // Sent already worded, from the same helper the projects list and the
            // workspace use.
            Assert.Contains("DocumentStatusPresentation.ForProject", model);
            Assert.Contains("StatusLabel", model);
        }

        [Fact]
        public void TheBrowserNoLongerKeepsItsOwnCopyOfTheEnum()
        {
            var script = Script();

            // The copy it kept was {0:Draft, 1:Active, 2:Closed, 3:Canceled}. The
            // enum is Draft=0, Active=1, Closed=2, Cancelled=3, OnHold=4 — so a
            // paused project rendered as the bare number "4", and "Canceled" with
            // one L matched no colour either.
            Assert.DoesNotContain("3: \"Canceled\"", script);
            Assert.DoesNotContain("2: \"Closed\",", script);
        }

        [Fact]
        public void EveryProjectStatusHasAWordAndAColour()
        {
            // The check the browser's copy failed: every value of the enum, not
            // the four somebody remembered.
            foreach (var status in Enum.GetValues<ProjectStatus>())
            {
                var shown = DocumentStatusPresentation.ForProject(status);

                Assert.False(string.IsNullOrWhiteSpace(shown.Label), $"{status} has no label");
                Assert.False(string.IsNullOrWhiteSpace(shown.Tone), $"{status} has no tone");

                // And the label is words, not the enum spelling.
                if (status == ProjectStatus.OnHold)
                    Assert.Equal("On hold", shown.Label);
            }
        }

        [Fact]
        public void AProjectWithNoStatusIsNotShownAsADraft()
        {
            // The view type this page reads makes the status nullable although a
            // project always has one. Defaulting a missing value to Draft would
            // put a different project on the screen than the one in the database.
            Assert.Contains("\"Unknown\"", PageModel());
        }

        // ===================================================== create project

        [Fact]
        public void CreatingFromAClientUsesTheSameFlowAsAnywhereElse()
        {
            var projects = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "Pages", "Projects.cshtml.cs"));

            // One creation path, launched with the customer already known —
            // rather than a second create-project UI living on the client page
            // with its own idea of what a project needs.
            Assert.Contains("ForCustomerId", projects);
            Assert.Contains("CustomerPreselected", projects);
            Assert.Contains("BuildCreateProjectModal(autoOpen: CustomerPreselected)", projects);

            // And the client page just says which client.
            Assert.Contains("'/Projects?ForCustomerId='", Script());
        }

        [Fact]
        public void APreselectedCustomerMustBeOneTheUserCanActuallyPick()
        {
            var projects = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "Pages", "Projects.cshtml.cs"));

            // A guid in the query string is not permission to use it: it is only
            // honoured when it is already in the list this user was offered.
            Assert.Contains("CustomerOptions.Any(o => o.Value == customerId.ToString())", projects);
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
