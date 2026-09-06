using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// Client details, measured at 360, 390, 768, 1024 and 1440 with a client
    /// that has three addresses, three contacts, three e-mail addresses and
    /// projects with real German titles. Thin seed data hid every one of these.
    ///
    /// What the browser reported before:
    ///
    ///   * the page scrolled sideways on a phone — 33px of horizontal overflow,
    ///     from a four-column projects table 361px wide in a 360px viewport,
    ///     with the Action column cut off the screen;
    ///   * the star, edit and delete controls on every address and contact row
    ///     were 16px tall, and the section actions 25px, because the icon-button
    ///     rule set `padding: 0`. A glyph is not a tap target;
    ///   * the Basic Information card had no card body — the only card on the
    ///     page without one — so its content sat on the card's own edge and the
    ///     three cards at the top of the page read as one broken panel;
    ///   * a long e-mail address was painted outside the chip drawn around it;
    ///   * "Last sync" printed a raw ISO string, milliseconds and all.
    /// </summary>
    public class ClientDetailsFitsTheScreenTests
    {
        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        private static string Read(params string[] parts)
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(
                new[] { root!.FullName }.Concat(parts).ToArray()));
        }

        private static string Page() => Read("WitcherHub", "Pages", "Clients", "Details.cshtml");
        private static string Script() => Read("WitcherHub", "wwwroot", "js", "pages", "clients", "clients.js");
        private static string Styles() => Read("WitcherHub", "wwwroot", "css", "site.css");

        /// <summary>
        /// Every declaration that reaches <paramref name="selector"/>, joined.
        ///
        /// All of them, not the first: a selector usually appears in more than
        /// one rule — here the icon button's reset and its sizing are written
        /// separately — and returning whichever came first would test one of
        /// them and report on the other.
        /// </summary>
        private static string RuleFor(string selector)
        {
            var css = Regex.Replace(Styles(), @"/\*.*?\*/", "", RegexOptions.Singleline);
            var joined = new System.Text.StringBuilder();

            foreach (Match rule in Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
            {
                var selectors = rule.Groups["sel"].Value.Split(',').Select(s => s.Trim());
                if (selectors.Contains(selector))
                    joined.Append(rule.Groups["body"].Value).Append('\n');
            }

            return joined.ToString();
        }

        // ============================================== nothing scrolls sideways

        [Fact]
        public void TheProjectsTableScrollsInsideItsOwnBoxRatherThanTakingThePageWithIt()
        {
            var page = Page();

            var wrapper = Regex.Match(page, @"<div id=""vc-projectsTableWrap""[^>]*>");
            Assert.True(wrapper.Success, "the projects table lost its wrapper");

            Assert.Contains("table-responsive", wrapper.Value);
        }

        [Fact]
        public void ADateRangeIsOneLineRatherThanFive()
        {
            // "2026-01-15 → 2026-09-30" broken at every hyphen is five lines for
            // one fact. The name may wrap — it is prose — the rest may not.
            var status = RuleFor(".vc-projects-table td:nth-child(2)");

            Assert.Contains("white-space: nowrap", status);
        }

        [Fact]
        public void TheProjectNameKeepsEnoughRoomToBeRead()
        {
            Assert.Matches(@"min-width:\s*\d", RuleFor(".vc-projects-table td:first-child"));
        }

        [Fact]
        public void ALongEmailStaysInsideTheChipDrawnAroundIt()
        {
            // The chip is a flex row of [kind][address]. A flex item will not
            // shrink below its content's intrinsic width unless it is allowed
            // to, and an e-mail address is one long unbreakable token.
            var address = RuleFor("#vc-emailList > span > span");

            Assert.Matches(@"min-width:\s*0", address);
            Assert.Contains("overflow-wrap: anywhere", address);
        }

        [Fact]
        public void ARowOfActionsWrapsRatherThanPushingTheAddressOffTheScreen()
        {
            // Four buttons at a usable size is 140px of controls. Beside a
            // German street address on a 360px screen they do not both fit.
            var actions = RuleFor(".vc-row-actions");

            Assert.Contains("flex-wrap: wrap", actions);
            Assert.Matches(@"#vc-addressList \.flex-grow-1|#vc-contactList \.flex-grow-1",
                Regex.Replace(Styles(), @"/\*.*?\*/", "", RegexOptions.Singleline));
        }

        // ============================================== you can hit the buttons

        [Fact]
        public void AnIconButtonIsBigEnoughToPress()
        {
            var icon = RuleFor(".btn.vc-icon-btn");

            var minWidth = Regex.Match(icon, @"min-width:\s*(?<px>\d+)px");
            var minHeight = Regex.Match(icon, @"min-height:\s*(?<px>\d+)px");

            Assert.True(minWidth.Success && minHeight.Success,
                "the icon button states no minimum size, so it is as big as its glyph");

            Assert.True(int.Parse(minWidth.Groups["px"].Value) >= 32);
            Assert.True(int.Parse(minHeight.Groups["px"].Value) >= 32);
        }

        [Fact]
        public void ThereIsOneSpellingOfABorderlessIconControl()
        {
            // `btn p-0 border-0 bg-transparent` and `btn vc-icon-btn` were two
            // ways of writing the same thing, and only one of them had a size.
            Assert.DoesNotContain("btn p-0 border-0", Page());
            Assert.DoesNotContain("btn p-0 border-0", Script());
        }

        [Fact]
        public void ABorderlessButtonStillAnswersTheMouseAndTheKeyboard()
        {
            var css = Regex.Replace(Styles(), @"/\*.*?\*/", "", RegexOptions.Singleline);

            Assert.Contains(".btn.vc-icon-btn:hover", css);
            Assert.Contains(".btn.vc-icon-btn:focus-visible", css);
        }

        // ============================================== the page reads properly

        [Fact]
        public void EveryCardOnThePageHasABody()
        {
            var lines = Page().Split('\n');
            var offenders = new List<int>();

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("class=\"card ")) continue;

                var next = string.Join(" ", lines
                    .Skip(i + 1)
                    .Take(3)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0));

                if (!next.Contains("card-body")) offenders.Add(i + 1);
            }

            Assert.True(offenders.Count == 0,
                "cards whose content sits on the card's own edge, at line(s): "
                + string.Join(", ", offenders));
        }

        [Fact]
        public void TheInternalIdIsReferenceMaterialRatherThanAHeading()
        {
            var page = Page();

            var id = Regex.Match(page, @"<span[^>]*id=""vc-idText""[^>]*>");
            Assert.True(id.Success, "the client id is gone from the header");

            // Set at body size next to the name it was the second loudest thing
            // on the page, and 36 characters wrapped around the type badge.
            Assert.Contains("small", id.Value);
            Assert.Contains("text-muted", id.Value);

            Assert.Contains("overflow-wrap: anywhere", RuleFor(".vc-client-id"));
        }

        [Fact]
        public void ATimestampIsWrittenForAPersonToRead()
        {
            var fmt = Regex.Match(
                Script(),
                @"function fmtDate\(v\) \{(?<body>.*?)\n    \}",
                RegexOptions.Singleline);

            Assert.True(fmt.Success, "the date formatter is gone");

            // Was `d.toISOString()` with the T and Z swapped for spaces, which
            // put "2026-09-03 07:59:36.957 UTC" on the page.
            Assert.DoesNotContain("toISOString", fmt.Groups["body"].Value);
            Assert.Contains("toLocaleString", fmt.Groups["body"].Value);
        }
    }
}
