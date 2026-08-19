using System.Globalization;
using System.Text;
using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// Where a fact in the contract came from.
    /// </summary>
    public enum CoverageOrigin
    {
        /// <summary>A confirmed contract position.</summary>
        Position = 0,

        /// <summary>A commercial term a person reviewed and ticked.</summary>
        Term = 1,

        /// <summary>The project or contract record.</summary>
        Record = 2,

        /// <summary>A topic found in the text somebody pasted in.</summary>
        Source = 3
    }

    /// <summary>
    /// One thing the contract has to account for.
    ///
    /// The ids exist so that "did the generated document cover everything?" is a
    /// question with an answer rather than an impression. They are internal: they
    /// are never written into a contract, never shown to a customer, and never
    /// rendered in the preview. They appear in the prompt, in the audit and in the
    /// log, and nowhere else.
    /// </summary>
    /// <param name="Id">Stable within one generation run: POS-003, TRM-002, SRC-011.</param>
    /// <param name="Origin">Which kind of source this came from.</param>
    /// <param name="Topic">A short German label — what a reader would call this.</param>
    /// <param name="Detail">The fact itself, as the contract must reflect it.</param>
    /// <param name="Evidence">
    /// Strings that must appear literally in the finished document if this item is
    /// genuinely covered. Figures and dates, mostly: a paraphrase of a price is a
    /// different price. Empty for items whose coverage can only be judged by topic.
    /// </param>
    /// <param name="IsCommercial">
    /// True for money, quantities, cycles and dates. These may never be invented,
    /// reworded into a different number, or quietly dropped; an uncovered one is a
    /// defect rather than a stylistic choice.
    /// </param>
    public sealed record CoverageItem(
        string Id,
        CoverageOrigin Origin,
        string Topic,
        string Detail,
        IReadOnlyList<string> Evidence,
        bool IsCommercial = false);

    /// <summary>
    /// Everything one contract must account for, enumerated before a word is
    /// written.
    ///
    /// The generator used to be given the data and a list of seven headings, and
    /// whatever came back was the contract. Nothing compared the two, so a
    /// position whose scope, acceptance criteria and exclusions were all silently
    /// dropped produced a contract that looked perfectly finished and was missing
    /// most of what had been agreed.
    ///
    /// The deterministic part of this ledger — positions, terms, the record — is
    /// built here in code and cannot be missed, reordered or hallucinated. Topics
    /// found in pasted text are added separately, because reading free prose is
    /// the one part of this that needs a model.
    /// </summary>
    public sealed class ContractCoverageLedger
    {
        private readonly List<CoverageItem> _items;

        private ContractCoverageLedger(List<CoverageItem> items) => _items = items;

        public IReadOnlyList<CoverageItem> Items => _items;

        public int Count => _items.Count;

        public IEnumerable<CoverageItem> Commercial => _items.Where(i => i.IsCommercial);

        public CoverageItem? this[string id] =>
            _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        // ==================================================== building

        /// <summary>
        /// The part of the ledger that comes from the record. No model involved,
        /// so it is the same list every time for the same data.
        /// </summary>
        public static ContractCoverageLedger FromRecord(ContractGenerationContext context)
        {
            var items = new List<CoverageItem>();
            var culture = CultureInfo.GetCultureInfo("de-DE");

            // ---- the project and the contract itself ------------------------

            var record = 0;

            void Record(string topic, string detail, IReadOnlyList<string>? evidence = null, bool commercial = false)
            {
                if (string.IsNullOrWhiteSpace(detail)) return;
                items.Add(new CoverageItem(
                    $"REC-{++record:D3}", CoverageOrigin.Record, topic, detail.Trim(),
                    evidence ?? Array.Empty<string>(), commercial));
            }

            Record("Projekt", context.Project.Title ?? "");
            Record("Projektbeschreibung", context.Project.Description ?? "");

            if (context.Contract.StartDate is { } contractStart)
                Record("Vertragsbeginn", contractStart.ToString("dd.MM.yyyy", culture),
                    [contractStart.ToString("dd.MM.yyyy", culture)], commercial: true);

            if (context.Contract.EndDate is { } contractEnd)
                Record("Vertragsende", contractEnd.ToString("dd.MM.yyyy", culture),
                    [contractEnd.ToString("dd.MM.yyyy", culture)], commercial: true);

            if (context.Contract.AgreedTotalNet is { } agreed)
                Record("Vereinbarte Nettosumme",
                    Money(agreed, context.Contract.Currency, culture),
                    [Amount(agreed, culture)], commercial: true);

            if (context.Contract.VatRatePercent is { } vat)
                Record("Umsatzsteuersatz", $"{Amount(vat, culture)} %",
                    [Amount(vat, culture)], commercial: true);

            Record("Zahlungsbedingungen", context.Contract.PaymentTerms ?? "");
            Record("Einleitung", context.Contract.Introduction ?? "");

            // ---- the totals -------------------------------------------------

            if (context.Totals is { PositionCount: > 0 } totals)
            {
                var currency = totals.Currency;

                items.Add(new CoverageItem(
                    "TOT-001", CoverageOrigin.Record, "Gesamtvergütung",
                    $"Netto {Money(totals.Subtotal, currency, culture)}, " +
                    $"Umsatzsteuer {Money(totals.Vat, currency, culture)}, " +
                    $"Gesamt {Money(totals.Total, currency, culture)}" +
                    (totals.Discount > 0m ? $", Rabatt {Money(totals.Discount, currency, culture)}" : ""),
                    [Amount(totals.Subtotal, culture), Amount(totals.Total, culture)],
                    IsCommercial: true));
            }

            // ---- one group per position -------------------------------------

            foreach (var position in context.Positions.OrderBy(p => p.Position))
                items.AddRange(ForPosition(position, culture));

            // ---- the reviewed commercial terms ------------------------------

            var term = 0;

            foreach (var confirmed in context.ConfirmedTerms)
            {
                if (string.IsNullOrWhiteSpace(confirmed.Value)) continue;

                items.Add(new CoverageItem(
                    $"TRM-{++term:D3}", CoverageOrigin.Term,
                    confirmed.Label,
                    $"{confirmed.Label}: {confirmed.Value.Trim()}",
                    // A term a person ticked out of a document is authoritative,
                    // but its value is prose as often as it is a figure, so only
                    // the figures are held to a literal match.
                    LooksNumeric(confirmed.Value) ? [confirmed.Value.Trim()] : Array.Empty<string>(),
                    IsCommercial: LooksNumeric(confirmed.Value)));
            }

            return new ContractCoverageLedger(items);
        }

        /// <summary>
        /// Adds the topics read out of a pasted document.
        ///
        /// Separate from <see cref="FromRecord"/> because these are the one part of
        /// the ledger that cannot be derived: a pasted agreement is prose, and
        /// finding what it obliges anybody to do means reading it. The ids continue
        /// the same numbering scheme so the audit does not care where an item came
        /// from.
        /// </summary>
        public ContractCoverageLedger WithSourceTopics(IEnumerable<SourceTopic> topics)
        {
            var items = new List<CoverageItem>(_items);
            var source = 0;

            foreach (var topic in topics)
            {
                if (string.IsNullOrWhiteSpace(topic.Topic)) continue;

                items.Add(new CoverageItem(
                    $"SRC-{++source:D3}", CoverageOrigin.Source,
                    topic.Topic.Trim(),
                    string.IsNullOrWhiteSpace(topic.Detail) ? topic.Topic.Trim() : topic.Detail.Trim(),
                    // Deliberately no literal evidence. A figure in a pasted
                    // document is the one thing that must NOT be carried over
                    // verbatim: the record outranks it, and requiring it to appear
                    // would be requiring the contract to contradict itself.
                    Array.Empty<string>()));
            }

            return new ContractCoverageLedger(items);
        }

        /// <summary>A topic a model found in pasted text. Ids are assigned here, not by the model.</summary>
        public sealed record SourceTopic(string Topic, string? Detail);

        // ==================================================== presentation

        /// <summary>
        /// The ledger as the model sees it: id, what it is, what it says.
        ///
        /// Compact on purpose — this goes into every prompt of the run, and a
        /// ledger that crowds out the request is a ledger that gets ignored.
        /// </summary>
        public string ToPromptList(IEnumerable<CoverageItem>? subset = null)
        {
            var text = new StringBuilder();

            foreach (var item in subset ?? _items)
            {
                text.Append(item.Id).Append(" [").Append(item.Topic).Append("] ");
                text.AppendLine(Collapse(item.Detail));
            }

            return text.ToString();
        }

        // ==================================================== helpers

        private static IEnumerable<CoverageItem> ForPosition(ManualPositionDto p, CultureInfo culture)
        {
            var n = p.Position;
            var name = string.IsNullOrWhiteSpace(p.Title) ? $"Position {n}" : p.Title.Trim();
            var slot = 0;

            CoverageItem Item(string topic, string detail, IReadOnlyList<string>? evidence = null, bool commercial = false) =>
                new($"POS-{n:D3}-{++slot:D2}", CoverageOrigin.Position, $"{name} – {topic}",
                    detail, evidence ?? Array.Empty<string>(), commercial);

            // What it is. Always present, because a position with no description
            // still has to appear in the Leistungsumfang.
            yield return Item("Leistung",
                Join(name, p.ServiceType, p.Description),
                [name]);

            if (!string.IsNullOrWhiteSpace(p.Scope))
                yield return Item("Umfang", p.Scope!.Trim());

            if (p.Deliverables.Count > 0)
                yield return Item("Liefergegenstände", Bullets(p.Deliverables));

            if (p.AcceptanceCriteria.Count > 0)
                yield return Item("Abnahmekriterien", Bullets(p.AcceptanceCriteria));

            if (p.CustomerResponsibilities.Count > 0)
                yield return Item("Mitwirkung des Auftraggebers", Bullets(p.CustomerResponsibilities));

            if (p.Assumptions.Count > 0)
                yield return Item("Annahmen", Bullets(p.Assumptions));

            if (p.Exclusions.Count > 0)
                yield return Item("Ausschlüsse", Bullets(p.Exclusions));

            if (!string.IsNullOrWhiteSpace(p.Notes))
                yield return Item("Hinweise", p.Notes!.Trim());

            if (!string.IsNullOrWhiteSpace(p.DeliveryMethod))
                yield return Item("Leistungserbringung", p.DeliveryMethod!.Trim());

            if (p.StartDate is { } start)
                yield return Item("Beginn", start.ToString("dd.MM.yyyy", culture),
                    [start.ToString("dd.MM.yyyy", culture)], commercial: true);

            if (p.DeliveryDate is { } delivery)
                yield return Item("Liefertermin", delivery.ToString("dd.MM.yyyy", culture),
                    [delivery.ToString("dd.MM.yyyy", culture)], commercial: true);

            // The commercial line, kept as one item so a contract cannot cover the
            // price while dropping the cycle it is charged on.
            var money = new StringBuilder();

            if (p.IsFree)
            {
                money.Append("ohne Berechnung");
            }
            else
            {
                money.Append(Amount(p.Quantity, culture));
                if (!string.IsNullOrWhiteSpace(p.Unit)) money.Append(' ').Append(p.Unit!.Trim());
                money.Append(" zu ").Append(Money(p.UnitPrice ?? 0m, p.Currency, culture));

                if (p.DiscountValue is > 0m)
                    money.Append(", Rabatt ").Append(Amount(p.DiscountValue.Value, culture))
                         .Append(p.DiscountType?.ToString() == "Percent" ? " %" : " " + p.Currency);

                money.Append(", netto ").Append(Money(p.NetTotal, p.Currency, culture));

                if (p.VatRate is { } rate)
                    money.Append(", USt ").Append(Amount(rate, culture)).Append(" %");
            }

            money.Append(", Abrechnung ").Append(BillingCycleGerman(p.BillingCycle.ToString()));

            if (p.DurationPeriods is > 0)
                money.Append(", Laufzeit ").Append(p.DurationPeriods).Append(" Perioden");

            var evidence = p.IsFree
                ? new List<string>()
                : new List<string> { Amount(p.NetTotal, culture) };

            yield return Item("Vergütung", money.ToString(), evidence, commercial: true);
        }

        private static string Join(params string?[] parts) =>
            string.Join(" — ", parts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        private static string Bullets(IEnumerable<string> values) =>
            string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));

        private static string Collapse(string text) =>
            text.Replace('\r', ' ').Replace('\n', ' ').Trim();

        /// <summary>German money, as the contract writes it: 1.234,56 EUR.</summary>
        internal static string Money(decimal value, string? currency, CultureInfo culture) =>
            $"{Amount(value, culture)} {(string.IsNullOrWhiteSpace(currency) ? "EUR" : currency)}";

        internal static string Amount(decimal value, CultureInfo culture) =>
            value.ToString("#,##0.00", culture);

        private static bool LooksNumeric(string value) =>
            value.Any(char.IsDigit);

        /// <summary>
        /// The billing cycle in the language the contract is written in. The enum
        /// name is English and belongs in the database, not in a German document.
        /// </summary>
        internal static string BillingCycleGerman(string cycle) => cycle switch
        {
            "OneTime" => "einmalig",
            "Monthly" => "monatlich",
            "Quarterly" => "vierteljährlich",
            "SemiAnnual" or "HalfYearly" => "halbjährlich",
            "Yearly" or "Annual" => "jährlich",
            "Weekly" => "wöchentlich",
            "Daily" => "täglich",
            _ => cycle
        };
    }
}
