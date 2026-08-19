namespace WitcherHub.Tests
{
    /// <summary>
    /// Wording can be read before it is approved.
    ///
    /// Reported as "i can not see the generated contract. Why should I approve
    /// the version before I can preview it?" — and the owner was right. The
    /// contract page rendered only <c>contract.Terms</c>, which approval sets,
    /// so the approve button was the only way to find out what a version said.
    /// Agreeing to text nobody can read first is backwards for any document,
    /// let alone the one that becomes a signed contract.
    ///
    /// Three routes now exist and are pinned here:
    ///   * every version row on the builder carries a View link,
    ///   * the contract page renders any version by number, behind a banner
    ///     that says it is a preview,
    ///   * a contract whose wording exists but is unapproved lists its versions
    ///     instead of claiming nothing was generated.
    /// </summary>
    public class VersionPreviewTests
    {
        private static string Positions() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Contracts", "Positions.cshtml"));

        private static string Details() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Contracts", "Details.cshtml"));

        private static string DetailsModel() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Contracts", "Details.cshtml.cs"));

        [Fact]
        public void EveryVersionRowLinksToItsPreview()
        {
            var page = Positions();

            var row = Between(page, "id=\"version-@d.Version\"", "</li>");
            Assert.NotEqual("", row);

            Assert.Contains("asp-route-version=\"@d.Version\"", row);
            Assert.Contains("/Contracts/Details", row);
        }

        [Fact]
        public void TheContractPageAcceptsAVersionToPreview()
        {
            var model = DetailsModel();

            Assert.Contains("public int? Version", model);

            // The preview must not require the version to be approved — that
            // requirement is the bug this exists to remove.
            Assert.Contains("GetDraftAsync(contract.Id, version", model);
        }

        [Fact]
        public void APreviewSaysItIsOne()
        {
            var view = Details();

            // A preview that looked identical to the approved contract would be
            // worse than no preview: someone would send it.
            Assert.Contains("Preview of version", view);
            Assert.Contains("not the approved wording", view);

            // And the way out of the preview is on the same banner.
            Assert.Contains("Show the approved wording", view);
        }

        [Fact]
        public void UnapprovedWordingIsListedRatherThanDeniedToExist()
        {
            var view = Details();

            // "Contract is not generated yet" was the wrong diagnosis when four
            // versions existed and none was approved, and it gave the reader
            // nothing to click.
            Assert.DoesNotContain("Contract is not generated yet", view);
            Assert.DoesNotContain("Contract is not generated yet", DetailsModel());

            Assert.Contains("No version has been approved yet", view);
            Assert.Contains("asp-route-version=\"@v.Version\"", view);
        }

        // ---------------------------------------------------------------

        private static string Between(string text, string from, string to)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return "";

            var end = text.IndexOf(to, start, StringComparison.Ordinal);
            return end < 0 ? text[start..] : text[start..end];
        }
    }
}
