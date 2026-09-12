using System.Globalization;
using System.Net;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    public static class ContractPdfHtmlBuilder
    {
        public sealed class ContractPdfParty
        {
            public string Name { get; init; } = "";
            public string InfoHtml { get; init; } = "";
        }

        public sealed class ContractPdfDocumentModel
        {
            public Guid ContractId { get; init; }
            public Guid ProjectId { get; init; }

            public string ContractNo { get; init; } = "";
            public string Currency { get; init; } = "EUR";
            public string StatusText { get; init; } = "";
            public string ProjectTitle { get; init; } = "";

            public DateTimeOffset CreatedAt { get; init; }
            public DateOnly? StartDate { get; init; }
            public DateOnly? EndDate { get; init; }

            public string SummaryText { get; init; } =
                "Dieser Vertrag regelt die vereinbarten Leistungen, Zuständigkeiten und Konditionen für das genannte Projekt.";

            public string DateRangeText { get; init; } = "—";
            public string TotalAmountDisplay { get; init; } = "—";
            public string NotesText { get; init; } =
                "Die Allgemeinen Geschäftsbedingungen werden separat bereitgestellt und im Rahmen des Signaturprozesses verlinkt.";

            public ContractPdfParty Provider { get; init; } = new();
            public ContractPdfParty Customer { get; init; } = new();

            public string ContractIntroHtml { get; init; } = "";
            public string ServicesSectionHtml { get; init; } = "";

            /// <summary>
            /// The contract's clauses — term, payment, liability, data protection
            /// and the rest. Empty renders no section, so a document that has no
            /// clauses beyond its subject matter does not grow an empty heading.
            /// </summary>
            public string TermsSectionHtml { get; init; } = "";

            public string PriceBoxHtml { get; init; } = "";

            public bool ShowSignaturePlaceholder { get; init; } = true;
        }

        public static string Build(ContractPdfDocumentModel m)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            string E(string? s) => WebUtility.HtmlEncode(s ?? "");

            string D(DateOnly? d)
                => d.HasValue
                    ? d.Value.ToString("dd.MM.yyyy", de)
                    : "—";

            var signaturePlaceholder = m.ShowSignaturePlaceholder
                ? $$"""
<div class="section">
  <div class="signature-placeholder">
    <h3>Unterschriften</h3>

    <div class="signature-grid">
      <div class="signature-box">
        <span class="signature-role">Anbieter</span>
        <span class="signature-party">{{E(m.Provider.Name)}}</span>
        <span class="signature-rule"></span>
        <span class="signature-caption">Ort, Datum, Unterschrift</span>
      </div>

      <div class="signature-box">
        <span class="signature-role">Kunde</span>
        <span class="signature-party">{{E(m.Customer.Name)}}</span>
        <span class="signature-rule"></span>
        <span class="signature-caption">Ort, Datum, Unterschrift</span>
      </div>
    </div>
  </div>
</div>
"""
                : string.Empty;
            // The banner used to repeat the sentence printed under the title, word
            // for word. A contract that says the same thing twice on its opening
            // screen reads as a template nobody proofread, so the banner names the
            // parties instead — which is the one thing the first screen was missing.
            var bannerSubtitle = E($"Zwischen {m.Provider.Name} und {m.Customer.Name}");
            if (!string.IsNullOrWhiteSpace(m.ContractNo))
                bannerSubtitle += " &middot; " + E($"Vertragsnummer {m.ContractNo}");

            // The template's own subject-matter and services blocks, drawn only
            // when there is something to put in them.
            //
            // They were unconditional, and the contract's own document now
            // carries both — a numbered "1. Leistungsbeschreibung" with the
            // scope under it, and a "2. Vergütung" with the price table. Drawn
            // regardless, the template restated the services as an empty
            // placeholder and printed the totals a second time, so the PDF
            // showed the same figures twice under two different headings. A
            // contract that states its own total twice is the same defect as a
            // contract that contains two documents.
            var introSection = !string.IsNullOrWhiteSpace(m.ContractIntroHtml)
                ? $$"""
<div class="section">
  <div class="section-head">
    <div>
      <h2>Vertragsgegenstand</h2>
      <p>Die nachfolgenden Leistungen und Projektbestandteile wurden zwischen den Parteien vereinbart.</p>
    </div>
  </div>

  <div class="rich-text">
    {{m.ContractIntroHtml}}
  </div>
</div>
"""
                : string.Empty;

            var priceBox = !string.IsNullOrWhiteSpace(m.PriceBoxHtml)
                ? $$"""
<div class="price-box">
  <h3>Preisübersicht</h3>
  {{m.PriceBoxHtml}}
</div>
"""
                : string.Empty;

            var servicesSection =
                !string.IsNullOrWhiteSpace(m.ServicesSectionHtml) || !string.IsNullOrWhiteSpace(m.PriceBoxHtml)
                ? $$"""
<div class="section">
  <div class="section-head">
    <div>
      <h2>Anlage A – Leistungsbeschreibung</h2>
      <p>Alle vereinbarten Positionen, Leistungsumfänge und Ergebnisse im Überblick.</p>
    </div>
  </div>

  <div class="rich-text">
    {{m.ServicesSectionHtml}}
  </div>

  {{priceBox}}
</div>
"""
                : string.Empty;

            // The clauses being signed. Given an id so the consent sentence at the
            // bottom of the signing page can point at it.
            var termsSection = !string.IsNullOrWhiteSpace(m.TermsSectionHtml)
                ? $$"""
<div class="section" id="vertragsbedingungen">
  <div class="section-head">
    <div>
      <h2>Vertragsbedingungen</h2>
      <p>Die nachstehenden Bestimmungen sind Bestandteil dieses Vertrages.</p>
    </div>
  </div>

  <div class="rich-text rich-text--terms">
    {{m.TermsSectionHtml}}
  </div>
</div>
"""
                : string.Empty;

            var notesSection = !string.IsNullOrWhiteSpace(m.NotesText)
    ? $$"""
<div class="section">
  <div class="contract-note">
    <h3>Hinweise</h3>
    <p>{{E(m.NotesText)}}</p>
  </div>
</div>
"""
    : string.Empty;
            return $$"""
<!doctype html>
<html data-no-external-header="1" lang="de">
<head>
  <meta charset="utf-8" />
  <style>
    /* Margins of a German business letter rather than of a web page: a wider
       binding edge on the left, even top and bottom. DIN 5008 puts the binding
       margin at 25mm; 22mm keeps a hole-punched copy readable without pushing
       the text block visibly off-centre. */
    @page {
      size: A4;
      margin: 20mm 18mm 20mm 22mm;
    }

    /* A contract is not a brochure.
     *
     * These tokens used to carry a violet accent, a lilac page ground and a
     * 50px drop shadow, and the document they produced read as marketing
     * material with a price in it. The palette is now ink on paper with grey
     * rules, which is what a German B2B contract looks like and what survives
     * being printed, photocopied and faxed to somebody's accountant.
     *
     * The names are kept so every rule that references them keeps working. */
    :root {
      --bg: #ffffff;
      --card: #ffffff;
      --text: #111111;
      --muted: #555555;
      --line: #d9d9d9;
      --line-strong: #b8b8b8;
      --primary: #111111;
      --primary-dark: #000000;
      --primary-soft: #f2f2f2;
      --primary-soft-2: #fafafa;
      --shadow: none;
      --radius: 3px;
    }

    * { box-sizing: border-box; }

    html, body {
      margin: 0;
      padding: 0;

      /* Arial first, and only faces that actually exist on the Linux container
         that prints the PDF behind it. Liberation Sans is metric-compatible
         with Arial, so the fallback sets to the same measure rather than
         reflowing the document, and both cover ä ö ü Ä Ö Ü ß. DejaVu Sans is
         last because it also carries Arabic, which matters while a customer's
         own name is in the document even though the contract text is German. */
      font-family: Arial, "Helvetica Neue", Helvetica, "Liberation Sans",
                   "DejaVu Sans", sans-serif;
      font-size: 10.5pt;
      background: var(--bg);
      color: var(--text);
      line-height: 1.45;
      -webkit-print-color-adjust: exact;
      print-color-adjust: exact;
    }

    .page {
      width: 210mm;
      min-height: 297mm;
      margin: 16px auto 28px;
      padding: 0;
    }

    .sheet {
      background: var(--card);
      border: 0;
      border-radius: 0;
      box-shadow: none;
      overflow: visible;
    }

    .top-bar {
      height: 0;
      background: none;
      margin-bottom: 0;
    }

    .content {
      padding: 22mm 16mm 16mm;
    }

    /* Two columns, the document on the left and its reference data on the right.

       This used to be a relatively positioned box with the meta card absolutely
       placed on top of it and a 258px right padding reserved by hand. The
       reserved gutter did not match the card (242px wide, pulled 28px further
       out), so the title ran underneath it: "AGENTURVERTRAG" was delivered to
       the customer reading "AGENTURVERTR". The print rules below already
       replaced this with a grid; the screen now uses the same shape. */
    .header {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 250px;
      gap: 24px;
      align-items: start;
      margin-bottom: 24px;
    }

    .header-left {
      display: flex;
      align-items: flex-start;
      gap: 16px;
      min-width: 0;
      max-width: 100%;
    }

    .logo-box {
      width: 78px;
      height: 78px;
      border-radius: 0;
      background: #ffffff;
      border: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
      flex-shrink: 0;
      box-shadow: none;
    }

    .logo-box img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
      padding: 10px;
    }

    .title-block {
  min-width: 0;
  padding-top: 2px;
}

    .title-block h1 {
      margin: 0 0 8px;
      font-size: 30px;
      line-height: 1.1;
      letter-spacing: -0.02em;
      color: #111111;
    }

    .title-block p {
      margin: 0;
      color: var(--muted);
      font-size: 13px;
    }

    /* The chips wrap. Held on one line they ran off the end of the title column
       and the last one — the contract's term — was cut in half. The print rules
       below already had to override this to wrap; now there is nothing to
       override. */
    .chip-row {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-top: 14px;
      max-width: 100%;
    }

    /* `flex: 0 0 auto` refused to let a chip shrink, so the project chip —
       which carries the whole project title — grew to whatever that title
       needed. On a phone it was measured 318px wider than the screen. It may
       shrink now, and its label wraps inside it. Same change the quote
       template already carries. */
    .chip {
      display: inline-flex;
      align-items: flex-start;
      gap: 6px;
      padding: 7px 10px;
      border-radius: 999px;
      font-size: 11.5px;
      line-height: 1.4;
      font-weight: 600;
      border: 1px solid var(--line-strong);
      background: #fff;
      color: var(--text);
      flex: 0 1 auto;
      min-width: 0;
      max-width: 100%;
      overflow-wrap: break-word;
    }

    .chip.project {
      background: var(--primary-soft);
      color: var(--primary-dark);
      border-color: #b8b8b8;
    }

    .chip.validity {
      background: #fafafa;
      color: #333333;
      border-color: #d9d9d9;
    }

    .meta {
      display: grid;
      gap: 8px;
      width: 100%;
      padding: 14px 16px;
      border: 1px solid var(--line-strong);
      border-radius: 0;
      background: #ffffff;
    }

    .meta-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  font-size: 12.5px;
  border-bottom: 1px dashed var(--line-strong);
  padding-bottom: 7px;
}

    .meta-row:last-child {
      border-bottom: 0;
      padding-bottom: 0;
    }

    .meta-row span:first-child {
      color: var(--muted);
    }

    .summary-banner {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: center;
      /* Ink on a light ground. The banner used to be a dark violet gradient,
         so everything in it was set in white; neutralising the background
         without the text left the project name, the parties and the contract
         total as white on near-white — present in the PDF and invisible on
         paper. */
      background: #f7f7f7;
      color: #111111;
      border: 0.5pt solid #d9d9d9;
      border-radius: 0;
      padding: 14px 18px;
      margin-bottom: 22px;
      flex-wrap: nowrap;
      box-shadow: none;
      break-inside: avoid;
    }

    .summary-banner h2 {
      margin: 0 0 5px;
      font-size: 14pt;
      color: #111111;
    }

    .summary-banner p {
      margin: 0;
      color: #333333;
      max-width: 520px;
      font-size: 9.5pt;
    }

    .summary-badge {
  background: #ffffff;
  border: 0.5pt solid #b8b8b8;
  border-radius: 0;
  padding: 12px 16px;
  min-width: 220px;
  text-align: center;
  box-shadow: none;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
}

    .summary-badge .label {
      display: block;
      font-size: 8.5pt;
      color: #555555;
      margin-bottom: 4px;
    }

    .summary-badge .value {
      font-size: 16pt;
      font-weight: bold;
      letter-spacing: 0;
      color: #111111;
      white-space: nowrap;
    }

    .party-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 14px;
      margin: 22px 0 24px;
    }

    .card {
      background: #ffffff;
      border: 1px solid var(--line-strong);
      border-radius: 0;
      padding: 16px;
    }

    .card-label {
      font-size: 11px;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--muted);
      margin-bottom: 8px;
      font-weight: 700;
    }

    .card h3 {
      margin: 0 0 10px;
      font-size: 19px;
      line-height: 1.25;
      color: #111111;
    }

    .info-list {
      display: grid;
      gap: 5px;
      color: #3f4756;
      font-size: 14px;
      line-height: 1.55;
    }

    .section {
      margin-top: 24px;
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      margin-bottom: 10px;
      flex-wrap: wrap;
    }

    .section-head h2 {
      margin: 0;
      font-size: 20px;
      color: #111111;
    }

    .section-head p {
      margin: 0;
      color: var(--muted);
      font-size: 13px;
    }

    .rich-text {
      border: 1px solid var(--line-strong);
      border-radius: 0;
      background: #fff;
      padding: 18px;
    }

    .rich-text h1,
    .rich-text h2,
    .rich-text h3,
    .rich-text h4 {
      color: #111111;
      margin-top: 0;
    }

    /* Contract prose. This is the text the customer has to read and agree to,
       so it is set at a reading size rather than at the caption size the cards
       and chips around it use.

       `overflow-wrap: anywhere` used to be on here, which let the browser break
       a long German compound anywhere at all — "Umsatzsteuer|behandlung" split
       across lines mid-word. Hyphenation does that properly, at the syllable. */
    .rich-text p {
      margin: 0 0 11px;
      color: #26203a;
      font-size: 15px;
      line-height: 1.72;
      overflow-wrap: break-word;
      hyphens: auto;
      -webkit-hyphens: auto;
    }

    .rich-text ul,
    .rich-text ol {
      margin: 0 0 12px 22px;
      padding-left: 4px;
      color: #26203a;
      font-size: 15px;
      line-height: 1.72;
    }

    .rich-text li {
      margin-bottom: 5px;
    }

    /* The clauses. Numbered paragraphs read as a column of "(1) (2) (3)", so a
       little more air between them than between lines within one. */
    .rich-text--terms h2,
    .rich-text--terms h3 {
      font-size: 16px;
      margin: 20px 0 8px;
      page-break-after: avoid;
      break-after: avoid;
    }

    .rich-text--terms h2:first-child,
    .rich-text--terms h3:first-child {
      margin-top: 0;
    }

    .rich-text--terms p {
      margin-bottom: 13px;
    }

    .contract-pos {
      border: 1px solid var(--line-strong);
      border-radius: 0;
      padding: 14px;
      background: #fafafa;
      margin-bottom: 14px;
    }

    .contract-pos:last-child {
      margin-bottom: 0;
    }

    .contract-pos__head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 12px;
      margin-bottom: 8px;
    }

    .contract-pos__head h3 {
      margin: 0;
      font-size: 16px;
      color: #111111;
    }

    .contract-pos__price {
      white-space: nowrap;
      font-size: 13px;
      font-weight: 800;
      color: #111111;
    }

    .contract-pos section {
      margin-top: 12px;
    }

    .contract-pos section h4 {
      margin: 0 0 6px;
      font-size: 13px;
      color: #333333;
    }

    .price-box {
      border: 1px solid var(--line-strong);
      border-radius: 0;
      background: #ffffff;
      padding: 16px;
      margin-top: 18px;
    }

    .price-box h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #111111;
    }

    .price-box table {
      width: 100%;
      border-collapse: collapse;
    }

    .price-box th,
    .price-box td {
      padding: 10px 8px;
      border-bottom: 1px dashed #d9d9d9;
      font-size: 14px;
      color: #26203a;
      vertical-align: top;
    }

    .price-box th {
      text-align: left;
      color: #333333;
      font-weight: 800;
    }

    .price-box td:last-child,
    .price-box th:last-child {
      text-align: right;
      white-space: nowrap;
    }

    .price-box tr:last-child td {
      border-bottom: 0;
    }

    .contract-note {
      margin-top: 18px;
      border: 1px solid var(--line-strong);
      border-radius: 0;
      padding: 16px;
      background: #fff;
    }

    .contract-note h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #111111;
    }

    .contract-note p {
      margin: 0;
      color: #4a4459;
      font-size: 14px;
      line-height: 1.65;
      white-space: pre-wrap;
    }

    .signature-placeholder {
      margin-top: 24px;
      border: 1px dashed #c4b5fd;
      border-radius: 0;
      padding: 16px;
      background: #fafafa;
      page-break-inside: avoid;
      break-inside: avoid;
    }

    .signature-placeholder h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #111111;
    }

    .signature-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .signature-box {
      border-top: 1px solid var(--line-strong);
      padding-top: 10px;
      min-height: 70px;
      color: #5b556a;
      font-size: 13px;
    }
    .pdf-footer {
  display: none;
}
    @media screen and (max-width: 980px) {
      .page {
        width: auto;
        min-height: auto;
        margin: 12px;
      }

      .header,
.summary-banner,
.party-grid,
.signature-grid {
  display: block;
}

.header {
  min-height: auto;
  padding-right: 0;
  padding-top: 0;
}

.meta,
.card,
.summary-badge {
  max-width: 100%;
  width: 100%;
  margin-top: 12px;
  position: static;
}
    }

   @media print {
  /* The margins the PDF actually gets.
   *
   * Chromium takes the page box from the document's own @page rule here — the
   * renderer asks for the CSS page size, and the margins come with it — so
   * this block, being the last @page in the sheet, is what every sheet of the
   * contract is set inside. Measured rather than assumed: a 90-paragraph
   * document paginates identically whether or not the renderer is also given
   * margins of its own, which it would not if these were being ignored.
   *
   * The wider left edge is the binding margin of a German business document. */
  @page {
    size: A4;
    margin: 20mm 18mm 20mm 22mm;
  }

  html, body {
    background: #fff !important;
    margin: 0 !important;
    padding: 0 !important;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  .page {
    width: auto !important;
    min-height: auto !important;
    margin: 0 !important;
    padding: 0 !important;
  }

  .sheet {
    background: #fff !important;
    border: 0 !important;
    border-radius: 0 !important;
    box-shadow: none !important;
    overflow: visible !important;
  }

  /* The accent bar is gone, so the space it used to need is gone with it.
     Left as a rule rather than removed so the element keeps a home if the
     letterhead ever wants one again. */
  .top-bar {
    height: 0 !important;
    margin: 0 !important;
  }

  .content {
    padding: 0 !important;
  }

  .header {
    display: grid !important;
    grid-template-columns: minmax(0, 1fr) 68mm;
    gap: 8mm;
    align-items: start;
    min-height: auto !important;
    padding: 0 !important;
    margin: 0 0 10mm 0 !important;
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .header-left {
    display: flex !important;
    align-items: flex-start;
    gap: 12px;
    min-width: 0;
    max-width: none;
    padding-right: 0 !important;
  }

  .title-block {
    min-width: 0;
    padding-top: 0 !important;
  }

  .title-block h1 {
    margin: 0 0 6px !important;
    font-size: 27px !important;
    line-height: 1.12 !important;
  }

  .title-block p {
    margin: 0 !important;
    font-size: 12px !important;
    line-height: 1.55 !important;
    max-width: none !important;
  }

  .chip-row {
    display: flex !important;
    flex-wrap: wrap !important;
    white-space: normal !important;
    gap: 6px !important;
    margin-top: 18px !important;
    padding-top: 2px !important;
    max-width: 100% !important;
  }

  .chip {
    font-size: 10.5px !important;
  }

  .meta {
    position: static !important;
    top: auto !important;
    right: auto !important;
    width: 68mm !important;
    max-width: 68mm !important;
    margin: 0 0 0 auto !important;
    padding: 12px 14px !important;
    display: grid !important;
    gap: 8px !important;
    border: 1px solid #b8b8b8 !important;
    border-radius: 16px !important;
    background: #fff !important;
    align-self: start !important;
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .meta-row {
    display: flex !important;
    justify-content: space-between !important;
    align-items: center !important;
    gap: 10px !important;
    font-size: 12px !important;
    border-bottom: 1px dashed #b8b8b8 !important;
    padding-bottom: 7px !important;
  }

  .meta-row:last-child {
    border-bottom: 0 !important;
    padding-bottom: 0 !important;
  }

  .summary-banner,
  .party-grid {
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .summary-badge {
    min-width: 56mm !important;
  }

  .summary-badge .value {
    font-size: 22px !important;
  }

  .section {
    margin-top: 18px !important;
  }

  .section-head {
    margin-bottom: 8px !important;
    page-break-after: avoid;
    break-after: avoid;
  }

  .section-head h2 {
    font-size: 18px !important;
  }

  .rich-text {
    border: 0 !important;
    background: transparent !important;
    padding: 0 !important;
    border-radius: 0 !important;
    box-shadow: none !important;
  }

  .rich-text p,
  .rich-text li {
    orphans: 3;
    widows: 3;
  }

  .contract-pos {
    margin: 0 0 12px 0 !important;
    padding: 0 !important;
    border: 0 !important;
    background: transparent !important;
    border-radius: 0 !important;
    box-shadow: none !important;
    page-break-inside: auto !important;
    break-inside: auto !important;
  }

  .contract-pos__head {
    margin-bottom: 6px !important;
    page-break-after: avoid;
    break-after: avoid;
  }

  .contract-pos__head h3 {
    font-size: 15px !important;
  }

  .contract-pos section {
    margin-top: 8px !important;
  }

  .contract-pos section h4 {
    page-break-after: avoid;
    break-after: avoid;
  }

  .price-box {
    margin-top: 14px !important;
    page-break-inside: avoid;
    break-inside: avoid;
    box-shadow: none !important;
  }

  .contract-note {
    page-break-inside: avoid;
    break-inside: avoid;
    box-shadow: none !important;
  }

  .signature-placeholder {
    margin-top: 16px !important;
    page-break-inside: avoid;
    break-inside: avoid;
    box-shadow: none !important;
  }

  .card,
  .meta,
  .summary-banner,
  .logo-box {
    box-shadow: none !important;
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   Geschäftsdokument — typography and pagination
   ═══════════════════════════════════════════════════════════════════════════

   Last in the sheet on purpose. Everything above sets a layout; this sets the
   text, and the two questions it answers are the ones a printed contract is
   actually judged on: can it be read, and does it break in sensible places.

   Sizes are in points because the destination is paper. A heading that is
   17.5px on a screen is a heading whose size nobody chose on an A4 page.
   ═══════════════════════════════════════════════════════════════════════════ */

.rich-text,
.rich-text--terms {
  font-size: 10.5pt;
  line-height: 1.45;
  color: #111;

  /* Not justified. Chromium justifies by stretching word spaces only — it has
     no hyphenation dictionary loaded for German here — and German compounds
     are long enough that a justified column opens rivers of white down the
     page. Ragged right is the lesser fault. */
  text-align: left;
}

.rich-text h1 { font-size: 16pt;   line-height: 1.25; margin: 0 0 18pt; }
.rich-text h2 { font-size: 12.5pt; line-height: 1.3;  margin: 18pt 0 7pt; }
.rich-text h3 { font-size: 11pt;   line-height: 1.35; margin: 12pt 0 5pt; }
.rich-text h4 { font-size: 10.5pt; line-height: 1.35; margin: 10pt 0 4pt; }

.rich-text p  { margin: 0 0 7pt; }
.rich-text ul,
.rich-text ol { margin: 0 0 7pt; padding-left: 16pt; }
.rich-text li { margin: 0 0 3pt; }

/* A heading is never the last thing on a page.
 *
 * "break-after: avoid" alone is not enough in Chromium: it keeps the heading
 * with the next box, but a paragraph whose first line lands alone at the foot
 * is the same defect one line later. The orphan and widow counts are what stop
 * that, and they apply to every paragraph rather than only to the first. */
.rich-text h1,
.rich-text h2,
.rich-text h3,
.rich-text h4 {
  break-after: avoid;
  page-break-after: avoid;
  break-inside: avoid;
}

.rich-text p,
.rich-text li {
  orphans: 3;
  widows: 3;
}

/* Price tables.
 *
 * The header repeats on every page the table reaches, which is what makes a
 * continued table readable rather than a grid of unlabelled numbers; a row is
 * never split down the middle. */
.rich-text table {
  width: 100%;
  border-collapse: collapse;
  margin: 10pt 0 12pt;
  font-size: 10pt;
}

.rich-text thead { display: table-header-group; }
.rich-text tfoot { display: table-footer-group; }
.rich-text tr    { break-inside: avoid; page-break-inside: avoid; }

.rich-text th,
.rich-text td {
  border: 0.5pt solid #b8b8b8;
  padding: 4pt 6pt;
  vertical-align: top;
  text-align: left;
}

.rich-text th {
  background: #f2f2f2;
  font-weight: bold;
}

/* Money right, and never broken.
 *
 * Markdown's ---: alignment reaches the cell as an inline style or an align
 * attribute depending on the renderer, so the last column is aligned here by
 * position as well. A figure and its currency belong on one line: "1.900,00"
 * at the end of one line and "EUR" at the start of the next is a number a
 * reader has to reassemble. */
.rich-text td:last-child,
.rich-text th:last-child,
.rich-text td[align="right"],
.rich-text th[align="right"] {
  text-align: right;
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}

/* Short logical units that must not be torn across a page. A signature block
   split so the lines are on one sheet and the names on the next is not a
   signature block. */
.signature-placeholder,
.signature-grid,
.signature-box,
.contract-note,
.price-box,
.meta {
  break-inside: avoid;
  page-break-inside: avoid;
}

/* The signature block, in the order a person signs it.
 *
 * It used to draw the rule above the names, with the empty space under it: a
 * line at the top of the box, a gap, and then "Anbieter / Netwitcher UG" —
 * which asks somebody to sign above a line they have not read the label of
 * yet, and leaves the blank space below the signature rather than above it.
 * Name first, then the space to sign in, then the rule, then what the rule is
 * for, which is how every German Vertrag ends. */
.signature-placeholder {
  margin-top: 16pt;
  border: 0;
  padding: 0;
  background: transparent;
}

.signature-placeholder h3 {
  font-size: 12.5pt;
  margin: 0 0 14pt;
}

.signature-box {
  display: block;
  border: 0;
  padding: 0;
}

.signature-role {
  display: block;
  font-size: 9pt;
  font-weight: bold;
  color: #555;
}

.signature-party {
  display: block;
  font-size: 10.5pt;
}

/* The space to sign in. 20mm is a signature's worth of room; the rule under it
   is what a pen is aimed at. */
.signature-rule {
  display: block;
  margin-top: 20mm;
  border-top: 0.5pt solid #111;
}

.signature-caption {
  display: block;
  margin-top: 3pt;
  font-size: 8.5pt;
  color: #555;
}

  </style>
</head>
<body>
  <div class="page">
    <div class="sheet">
      <div class="top-bar"></div>

      <div class="content">
        <div class="header">
          <div class="header-left">
            <div class="logo-box">
              <img src="__NETWITCHER_LOGO__" alt="Netwitcher logo" />
            </div>

            <div class="title-block">
              <h1>Agenturvertrag</h1>
              <p>{{E(m.SummaryText)}}</p>

              <div class="chip-row">
                <div class="chip project">Status: <strong>{{E(m.StatusText)}}</strong></div>
                <div class="chip project">Projekt: <strong>{{E(m.ProjectTitle)}}</strong></div>
                <div class="chip validity">Laufzeit: <strong>{{E(m.DateRangeText)}}</strong></div>
              </div>
            </div>
          </div>

          <div class="meta">
            <div class="meta-row">
              <span>Vertragsnummer</span>
              <strong>{{E(m.ContractNo)}}</strong>
            </div>
            <div class="meta-row">
              <span>Erstellt am</span>
              <strong>{{E(m.CreatedAt.ToString("dd.MM.yyyy", de))}}</strong>
            </div>
            <div class="meta-row">
              <span>Beginn</span>
              <strong>{{E(D(m.StartDate))}}</strong>
            </div>
            <div class="meta-row">
              <span>Ende</span>
              <strong>{{E(D(m.EndDate))}}</strong>
            </div>
          </div>
        </div>

        <div class="summary-banner">
          <div>
            <h2>{{E(m.ProjectTitle)}}</h2>
            <p>{{bannerSubtitle}}</p>
          </div>

          <div class="summary-badge">
            <span class="label">Gesamtwert</span>
            <div class="value">{{E(m.TotalAmountDisplay)}}</div>
          </div>
        </div>

        <div class="party-grid">
          <div class="card">
            <div class="card-label">Anbieter</div>
            <h3>{{E(m.Provider.Name)}}</h3>
            <div class="info-list">
              {{m.Provider.InfoHtml}}
            </div>
          </div>

          <div class="card">
            <div class="card-label">Kunde</div>
            <h3>{{E(m.Customer.Name)}}</h3>
            <div class="info-list">
              {{m.Customer.InfoHtml}}
            </div>
          </div>
        </div>

{{introSection}}
{{servicesSection}}
{{termsSection}}
{{notesSection}}

        {{signaturePlaceholder}}

      </div>
    </div>
  </div>

  <div class="pdf-footer">
  <div class="pdf-footer__inner">
    <span>Netwitcher UG (haftungsbeschränkt)</span>
    <span class="pdf-page-counter"></span>
  </div>
</div>
</body>
</html>
""";
        }

        /// <summary>
        /// The contract with the customer's signature block appended.
        ///
        /// Lived as a private method on the signing page, which meant the only
        /// way to produce a signed contract was to sign one. Contract details
        /// needs the same document to hand back on request, and two copies of
        /// this would be two documents that could drift apart — so it sits with
        /// the builder that renders the contract in the first place.
        /// </summary>
        public static string BuildSigned(
            ContractPdfDocumentModel model,
            string signerName,
            string signerEmail,
            DateTimeOffset signedAt,
            string signatureDataUrl,
            string logoUrl)
        {
            var html = Build(model);

            html = html.Replace("__NETWITCHER_LOGO__", logoUrl ?? "", StringComparison.OrdinalIgnoreCase);

            var extraStyle = """
<style>
  .signedContractBlock{
    margin: 24px 0 0 0;
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .signedContractCard{
    border: 1px solid #b8b8b8;
    border-radius: 0;
    padding: 18px 20px;
    background: #ffffff;
  }

  .signedContractTitle{
    font-size: 18px;
    font-weight: 800;
    color: #111111;
    margin: 0 0 14px 0;
  }

  .signedContractRow{
    margin: 6px 0;
    font-size: 12.5px;
    line-height: 1.6;
    color: #31263f;
  }

  .signedContractRow strong{
    display: inline-block;
    min-width: 120px;
    color: #333333;
  }

  .signedContractImage{
    margin-top: 14px;
  }

  .signedContractImage img{
    max-width: 260px;
    max-height: 120px;
    display: block;
  }

  .signedContractLine{
    width: 260px;
    border-top: 1px solid var(--line-strong);
    margin-top: 8px;
  }
</style>
""";

            var signatureBlock = $"""
<div class="signedContractBlock">
  <div class="signedContractCard">
    <h2 class="signedContractTitle">Kundenunterschrift</h2>
    <div class="signedContractRow"><strong>Vertrag:</strong> {WebUtility.HtmlEncode(model.ContractNo)}</div>
    <div class="signedContractRow"><strong>Projekt:</strong> {WebUtility.HtmlEncode(model.ProjectTitle)}</div>
    <div class="signedContractRow"><strong>Name:</strong> {WebUtility.HtmlEncode(signerName ?? "")}</div>
    <div class="signedContractRow"><strong>E-Mail:</strong> {WebUtility.HtmlEncode(signerEmail ?? "")}</div>
    <div class="signedContractRow"><strong>Signiert am:</strong> {WebUtility.HtmlEncode(signedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))}</div>

    <div class="signedContractImage">
      <img src="{WebUtility.HtmlEncode(signatureDataUrl ?? "")}" alt="Signature" />
      <div class="signedContractLine"></div>
    </div>
  </div>
</div>
""";

            if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</head>", extraStyle + "</head>", StringComparison.OrdinalIgnoreCase);
            else
                html = extraStyle + html;

            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</body>", signatureBlock + "</body>", StringComparison.OrdinalIgnoreCase);
            else
                html += signatureBlock;

            return html;
        }

    }
}