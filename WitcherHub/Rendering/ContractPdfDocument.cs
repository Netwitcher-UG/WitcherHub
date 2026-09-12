using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Pdf;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Rendering
{
    /// <summary>
    /// Turns a contract into the document model the PDF builder renders.
    ///
    /// This lived as a set of private methods on the signing page, which meant
    /// the only place that could produce a contract document was the page a
    /// customer signs on. Contract details needs the same document to hand back
    /// when somebody asks for the signed PDF, and a second copy of four hundred
    /// lines of price tables and section extraction would be a second document
    /// that could quietly disagree with the first.
    ///
    /// Nothing here reads page state — it is a function of the contract and the
    /// template options.
    /// </summary>
    public static class ContractPdfDocument
    {
        private static string NormalizeNewLines(string? s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\r\n", "\n");

        /// <summary>
        /// A contract entity, as the document the PDF builder renders.
        /// </summary>
        public static ContractPdfHtmlBuilder.ContractPdfDocumentModel Build(
            Contract contract,
            ContractTemplateOptions options,
            bool showSignaturePlaceholder = true,
            string? notesText = "")
        {
            var customer = contract.Project.Customer;

            var providerLines = NormalizeNewLines(options.ProviderBlock ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var providerName = providerLines.FirstOrDefault() ?? "Netwitcher";
            var providerInfoHtml = string.Join("", providerLines.Skip(1)
                .Select(x => $"<div>{WebUtility.HtmlEncode(x)}</div>"));

            var customerName = customer.Type == CustomerType.Individual
                ? BuildName(customer.FirstName, customer.LastName, customer.Name)
                : (customer.Name ?? string.Empty).Trim();

            var addr = customer.Addresses?
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            var email =
                customer.Contacts?
                    .OrderByDescending(c => c.IsPrimary)
                    .Select(c => c.Email)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? customer.EmailAddresses?
                    .Where(ea => ea.Kind == "business")
                    .Select(ea => ea.Email)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? customer.EmailAddresses?
                    .Select(ea => ea.Email)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            var customerInfoParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(addr?.StreetRaw))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(addr.StreetRaw)}</div>");

            var cityLine = ((addr?.PostalCode ?? "").Trim() + " " + (addr?.City ?? "").Trim()).Trim();
            if (!string.IsNullOrWhiteSpace(cityLine))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(cityLine)}</div>");

            if (!string.IsNullOrWhiteSpace(addr?.Country))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(addr.Country)}</div>");

            if (!string.IsNullOrWhiteSpace(email))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(email.Trim())}</div>");

            var structured = DeserializeStructured(contract.TermsStructured);

            // A document the composer wrote carries the whole contract: the
            // description of every position, the price table computed in code,
            // and the numbered general terms. The page's own subject-matter,
            // services and price blocks were written for documents that did not,
            // and drawing them over a composed one restated the services as an
            // empty placeholder and printed the totals a second time under a
            // different heading.
            //
            // So for a composed document the body is the document, and only the
            // parts the page genuinely owns — the letterhead, the party cards and
            // the signature block — are drawn around it.
            var composed = IsComposedDocument(contract.Terms);

            var introHtml = "";
            var servicesHtml = "";
            var priceBoxHtml = "";

            if (!composed)
            {
                var introMarkdown = ExtractMarkdownSection(contract.Terms, "## Vertragsgegenstand", "## Anlage A");
                if (string.IsNullOrWhiteSpace(introMarkdown))
                {
                    introMarkdown =
                        "Der Anbieter erbringt die für das genannte Projekt vereinbarten Leistungen gemäß Anlage A – Leistungsbeschreibung.";
                }

                introHtml = MarkdownToSafeHtml(introMarkdown);

                servicesHtml = structured is not null && structured.Positions is not null && structured.Positions.Count > 0
                    ? BuildServicesSectionHtml(structured, contract.Currency ?? "EUR")
                    : MarkdownToSafeHtml(
                        ExtractMarkdownSection(contract.Terms, "## Anlage A", "## Preisübersicht"),
                        "<p>Die vereinbarten Leistungen sind in den Vertragspositionen festgehalten.</p>");

                priceBoxHtml = BuildPriceBoxHtml(contract);
            }

            // The clauses the sections above do not cover. Without this the page
            // shows what is being bought and what it costs, and none of the terms
            // the signature is actually given on.
            //
            // The parties and the signature block are dropped in both cases: the
            // page draws its own, and a contract naming its parties twice on
            // consecutive pages reads as a document nobody proofread.
            var termsHtml = MarkdownToSafeHtml(
                ExtractRemainingTermsMarkdown(
                    contract.Terms,
                    "Vertragspartner",
                    "Unterschriften",
                    composed ? "" : "Vertragsgegenstand",
                    composed ? "" : "Anlage A",
                    composed ? "" : "Preisübersicht"),
                fallbackHtml: "");

            var (netTotal, taxTotal, grossTotal) = CalculateContractTotals(contract);

            var dateRange = BuildDateRangeText(contract.StartDate, contract.EndDate);
            var totalDisplay = FormatMoney(contract.Currency, grossTotal);

            return new ContractPdfHtmlBuilder.ContractPdfDocumentModel
            {
                ContractId = contract.Id,
                ProjectId = contract.ProjectId,
                ContractNo = contract.ContractNo ?? "",
                Currency = contract.Currency ?? "EUR",
                StatusText = contract.Status.ToString(),
                ProjectTitle = contract.Project?.Title ?? "",
                CreatedAt = contract.CreatedAt,
                StartDate = contract.StartDate,
                EndDate = contract.EndDate,
                SummaryText = "Dieser Vertrag regelt die vereinbarten Leistungen, Zuständigkeiten und Konditionen für das genannte Projekt.",
                DateRangeText = dateRange,
                TotalAmountDisplay = totalDisplay,
                NotesText = notesText ?? "",
                Provider = new ContractPdfHtmlBuilder.ContractPdfParty
                {
                    Name = providerName,
                    InfoHtml = providerInfoHtml
                },
                Customer = new ContractPdfHtmlBuilder.ContractPdfParty
                {
                    Name = customerName,
                    InfoHtml = string.Join("", customerInfoParts)
                },
                ContractIntroHtml = introHtml,
                ServicesSectionHtml = servicesHtml,
                TermsSectionHtml = termsHtml,
                PriceBoxHtml = priceBoxHtml,
                ShowSignaturePlaceholder = showSignaturePlaceholder
            };
        }

        private static ContractStructuredTermsDto? DeserializeStructured(JsonDocument? doc)
        {
            if (doc is null) return null;

            try
            {
                return JsonSerializer.Deserialize<ContractStructuredTermsDto>(
                    doc.RootElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        private static string BuildServicesSectionHtml(ContractStructuredTermsDto structured, string currency)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var sb = new StringBuilder();

            foreach (var p in (structured.Positions ?? new List<ContractPositionSpecDto>()).OrderBy(x => x.PositionNo))
            {
                var title = string.IsNullOrWhiteSpace(p.Title)
                    ? $"Position {p.PositionNo}"
                    : p.Title.Trim();

                var price = p.LineNetPrice.HasValue
                    ? $"{p.LineNetPrice.Value.ToString("N2", de)} {currency}"
                    : "";

                sb.AppendLine("""<div class="contract-pos">""");
                sb.AppendLine("""<div class="contract-pos__head">""");
                sb.AppendLine($"""<h3>Position {p.PositionNo}: {WebUtility.HtmlEncode(title)}</h3>""");

                if (!string.IsNullOrWhiteSpace(price))
                    sb.AppendLine($"""<div class="contract-pos__price">{WebUtility.HtmlEncode(price)}</div>""");

                sb.AppendLine("""</div>""");

                if (!string.IsNullOrWhiteSpace(p.Sections?.Scope))
                {
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Scope.Trim()) + "</p>");
                }

                AppendHtmlListSection(sb, "Liefergegenstände", p.Sections?.Deliverables);
                AppendHtmlListSection(sb, "Nicht enthalten", p.Sections?.OutOfScope);
                AppendHtmlListSection(sb, "Mitwirkungspflichten des Auftraggebers", p.Sections?.CustomerResponsibilities);
                AppendHtmlListSection(sb, "Abnahmekriterien", p.Sections?.AcceptanceCriteria);

                if (!string.IsNullOrWhiteSpace(p.Sections?.Timeline))
                {
                    sb.AppendLine("<section>");
                    sb.AppendLine("<h4>Zeitplan</h4>");
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Timeline.Trim()) + "</p>");
                    sb.AppendLine("</section>");
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Assumptions))
                {
                    sb.AppendLine("<section>");
                    sb.AppendLine("<h4>Annahmen</h4>");
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Assumptions.Trim()) + "</p>");
                    sb.AppendLine("</section>");
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Revisions))
                {
                    sb.AppendLine("<section>");
                    sb.AppendLine("<h4>Überarbeitungen</h4>");
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Revisions.Trim()) + "</p>");
                    sb.AppendLine("</section>");
                }

                sb.AppendLine("</div>");
            }

            return sb.Length == 0
                ? "<p>Keine Leistungspositionen vorhanden.</p>"
                : sb.ToString();
        }

        private static void AppendHtmlListSection(StringBuilder sb, string title, List<string>? items)
        {
            var clean = (items ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            if (clean.Count == 0) return;

            sb.AppendLine("<section>");
            sb.AppendLine($"<h4>{WebUtility.HtmlEncode(title)}</h4>");
            sb.AppendLine("<ul>");

            foreach (var item in clean)
                sb.AppendLine("<li>" + WebUtility.HtmlEncode(item) + "</li>");

            sb.AppendLine("</ul>");
            sb.AppendLine("</section>");
        }

        private static string BuildPriceBoxHtml(Contract contract)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var currency = string.IsNullOrWhiteSpace(contract.Currency) ? "EUR" : contract.Currency.Trim();

            var rows = (contract.Items ?? new List<ContractItem>())
                .OrderBy(x => x.Position)
                .Select(x => new
                {
                    Position = x.Position,
                    Title = string.IsNullOrWhiteSpace(x.Title) ? $"Position {x.Position}" : x.Title.Trim(),
                    Net = ResolveContractItemNetAmount(x)
                })
                .ToList();

            var (netTotal, taxTotal, grossTotal) = CalculateContractTotals(contract);

            var sb = new StringBuilder();
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Pos.</th><th>Bezeichnung</th><th>Netto</th></tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var row in rows)
            {
                sb.AppendLine($"""
<tr>
  <td>{row.Position}</td>
  <td>{WebUtility.HtmlEncode(row.Title)}</td>
  <td>{WebUtility.HtmlEncode(row.Net.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</td>
</tr>
""");
            }

            sb.AppendLine($"""
<tr>
  <td></td>
  <td><strong>Zwischensumme (Netto)</strong></td>
  <td><strong>{WebUtility.HtmlEncode(netTotal.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</strong></td>
</tr>
""");

            if (taxTotal > 0m)
            {
                sb.AppendLine($"""
<tr>
  <td></td>
  <td><strong>USt. 19%</strong></td>
  <td><strong>{WebUtility.HtmlEncode(taxTotal.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</strong></td>
</tr>
""");
            }

            sb.AppendLine($"""
<tr>
  <td></td>
  <td><strong>Gesamtbetrag</strong></td>
  <td><strong>{WebUtility.HtmlEncode(grossTotal.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</strong></td>
</tr>
""");

            sb.AppendLine("</tbody>");
            sb.AppendLine("</table>");

            sb.AppendLine("<p style=\"margin-top:10px;color:#5b556a;font-size:12px;\">");
            sb.AppendLine("Alle Beträge netto zzgl. gesetzlicher Umsatzsteuer, sofern nicht anders ausgewiesen.");
            sb.AppendLine("</p>");

            return sb.ToString();
        }

        private static (decimal Net, decimal Tax, decimal Gross) CalculateContractTotals(Contract contract)
        {
            var net = (contract.Items ?? new List<ContractItem>())
                .Sum(ResolveContractItemNetAmount);

            net = Math.Round(net, 2, MidpointRounding.AwayFromZero);

            var tax = contract.ApplyVat
                ? Math.Round(net * 0.19m, 2, MidpointRounding.AwayFromZero)
                : 0m;

            var gross = net + tax;
            return (net, tax, gross);
        }

        private static decimal ResolveContractItemNetAmount(ContractItem item)
        {
            if (item.AgreedPrice.HasValue && item.AgreedPrice.Value > 0m)
                return Math.Round(item.AgreedPrice.Value, 2, MidpointRounding.AwayFromZero);

            var total = ReadDecimal(item.PriceBreakdown, "total", 0m);
            if (total > 0m)
                return Math.Round(total, 2, MidpointRounding.AwayFromZero);

            var subTotal = ReadDecimal(item.PriceBreakdown, "subTotal", 0m);
            if (subTotal > 0m)
                return Math.Round(subTotal, 2, MidpointRounding.AwayFromZero);

            var fallback = item.Quantity * item.UnitPrice;
            return Math.Round(fallback, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal ReadDecimal(JsonDocument? doc, string prop, decimal fallback)
        {
            try
            {
                if (doc is null) return fallback;
                if (!doc.RootElement.TryGetProperty(prop, out var value)) return fallback;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d))
                    return d;

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string BuildDateRangeText(DateOnly? start, DateOnly? end)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            if (start.HasValue && end.HasValue)
                return $"{start.Value.ToString("dd.MM.yyyy", de)} – {end.Value.ToString("dd.MM.yyyy", de)}";

            if (start.HasValue)
                return $"ab {start.Value.ToString("dd.MM.yyyy", de)}";

            if (end.HasValue)
                return $"bis {end.Value.ToString("dd.MM.yyyy", de)}";

            return "—";
        }

        private static string FormatMoney(string? currency, decimal value)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim();

            return string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)
                ? value.ToString("N2", de) + " €"
                : value.ToString("N2", de) + " " + currency;
        }

        /// <summary>
        /// Everything in the contract that the sections above do not already show.
        ///
        /// The page was built from three named slices of the document —
        /// Vertragsgegenstand, Anlage A and Preisübersicht — and silently dropped
        /// the rest. The rest is the contract: term and notice period, payment
        /// terms, liability, confidentiality, data protection, rights of use,
        /// governing law. The customer was asked to tick "I have read the terms"
        /// on a page those terms were not on.
        ///
        /// So the document is split on its own "## " headings and every section
        /// that is not one of the three already rendered is returned, in the order
        /// the contract sets them. Nothing is filtered by content: a clause this
        /// method does not recognise is still a clause, and it is shown.
        /// </summary>
        /// <summary>
        /// Whether this document was written by the composer — recognised by the
        /// numbered sections it and nothing else produces.
        ///
        /// Deliberately a property of the text rather than a flag on the record:
        /// a contract stored years ago is rendered from its own wording, and
        /// asking the wording what shape it is keeps every stored document
        /// readable without a migration that would rewrite it.
        /// </summary>
        private static bool IsComposedDocument(string? markdown) =>
            !string.IsNullOrWhiteSpace(markdown) &&
            Regex.IsMatch(
                NormalizeNewLines(markdown!),
                @"^##\s+\d+\.\s+\S",
                RegexOptions.Multiline);

        /// <summary>
        /// Everything except the sections the page draws itself.
        ///
        /// A heading is matched with its number removed, so "2. Vergütung" is
        /// recognised as the price section the same way "Preisübersicht" was
        /// before the numbering existed — otherwise renaming the headings
        /// silently turns every already-shown section back on and the document
        /// states its totals twice.
        /// </summary>
        private static string ExtractRemainingTermsMarkdown(string? markdown, params string[] alreadyShown)
        {
            markdown = NormalizeNewLines(markdown ?? "").Trim();
            if (string.IsNullOrWhiteSpace(markdown)) return "";

            var lines = markdown.Split('\n');
            var kept = new StringBuilder();

            // Text before the first "## " heading is the document title block. It
            // is the page's own header here, so it is not repeated in the body.
            var keeping = false;
            var seenFirstSection = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    seenFirstSection = true;

                    var heading = Regex.Replace(
                        line[3..].Trim(), @"^\d+(?:\.\d+)*\.?\s*", "");

                    keeping = !alreadyShown.Any(shown =>
                        !string.IsNullOrEmpty(shown) &&
                        heading.StartsWith(shown, StringComparison.OrdinalIgnoreCase));

                    if (keeping) kept.Append(line).Append('\n');
                    continue;
                }

                if (!seenFirstSection) continue;
                if (keeping) kept.Append(line).Append('\n');
            }

            return kept.ToString().Trim();
        }

        private static string ExtractMarkdownSection(string? markdown, string startHeading, string? nextHeading)
        {
            markdown = NormalizeNewLines(markdown ?? "");

            if (string.IsNullOrWhiteSpace(markdown))
                return "";

            var start = markdown.IndexOf(startHeading, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return "";

            start = markdown.IndexOf('\n', start);
            if (start < 0) return "";

            start++;

            var end = !string.IsNullOrWhiteSpace(nextHeading)
                ? markdown.IndexOf(nextHeading, start, StringComparison.OrdinalIgnoreCase)
                : -1;

            if (end < 0) end = markdown.Length;

            return markdown.Substring(start, end - start).Trim();
        }

        private static string MarkdownToSafeHtml(string markdown, string? fallbackHtml = null)
        {
            markdown = NormalizeNewLines(markdown ?? "").Trim();

            if (string.IsNullOrWhiteSpace(markdown))
                return fallbackHtml ?? "<p>—</p>";

            return ContractMarkdown.ToHtml(markdown);
        }

        private static string BuildName(string? first, string? last, string? fallback)
        {
            var f = (first ?? string.Empty).Trim();
            var l = (last ?? string.Empty).Trim();
            var both = (f + " " + l).Trim();

            return string.IsNullOrWhiteSpace(both)
                ? (fallback ?? string.Empty).Trim()
                : both;
        }
    }
}
