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
        <strong>Anbieter</strong><br />
        {{E(m.Provider.Name)}}
      </div>

      <div class="signature-box">
        <strong>Kunde</strong><br />
        {{E(m.Customer.Name)}}
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
    @page {
      size: A4;
      margin: 14mm;
    }

    :root {
      --bg: #f7f4ff;
      --card: #ffffff;
      --text: #1f1630;
      --muted: #746a86;
      --line: #e7def7;
      --line-strong: #d7c7f3;
      --primary: #7c3aed;
      --primary-dark: #5b21b6;
      --primary-soft: #f3e8ff;
      --primary-soft-2: #faf5ff;
      --shadow: 0 18px 50px rgba(91, 33, 182, 0.10);
      --radius: 22px;
    }

    * { box-sizing: border-box; }

    html, body {
      margin: 0;
      padding: 0;

      /* Every face here exists somewhere this document is rendered: the
         customer's browser, and the Linux container that prints the PDF. The
         previous stack led with two fonts neither of those has installed and
         fell through to Tahoma, which is not a face to set a contract in. */
      font-family: "Segoe UI", Roboto, "Helvetica Neue", "Liberation Sans",
                   "DejaVu Sans", Arial, sans-serif;
      font-size: 15px;
      background:
        radial-gradient(circle at top left, #f3e8ff 0, transparent 30%),
        radial-gradient(circle at bottom right, #ede9fe 0, transparent 26%),
        var(--bg);
      color: var(--text);
      line-height: 1.55;
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
      border: 1px solid rgba(91, 33, 182, 0.08);
      border-radius: 28px;
      box-shadow: var(--shadow);
      overflow: hidden;
    }

    .top-bar {
      height: 8px;
      background: linear-gradient(90deg, #6d28d9, #7c3aed, #8b5cf6, #a855f7);
      margin-bottom: 14px;
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
      border-radius: 18px;
      background: linear-gradient(180deg, #ffffff, #faf5ff);
      border: 1px solid var(--line-strong);
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
      flex-shrink: 0;
      box-shadow: 0 8px 24px rgba(91, 33, 182, 0.05);
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
      color: #24163f;
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

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 7px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 600;
      border: 1px solid var(--line-strong);
      background: #fff;
      color: var(--text);
      flex: 0 0 auto;
    }

    .chip.project {
      background: var(--primary-soft);
      color: var(--primary-dark);
      border-color: #d8b4fe;
    }

    .chip.validity {
      background: #faf5ff;
      color: #6b21a8;
      border-color: #e9d5ff;
    }

    .meta {
      display: grid;
      gap: 8px;
      width: 100%;
      padding: 14px 16px;
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      background: linear-gradient(180deg, #ffffff, #fcfaff);
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
      background: linear-gradient(135deg, #4c1d95, #6d28d9 55%, #7c3aed);
      color: #fff;
      border-radius: 22px;
      padding: 18px 20px;
      margin-bottom: 24px;
      flex-wrap: nowrap;
      box-shadow: 0 16px 36px rgba(109, 40, 217, 0.20);
    }

    .summary-banner h2 {
      margin: 0 0 5px;
      font-size: 20px;
      color: #fff;
    }

    .summary-banner p {
      margin: 0;
      color: rgba(255,255,255,0.86);
      max-width: 520px;
      font-size: 13px;
    }

    .summary-badge {
  background: rgba(255,255,255,0.14);
  border: 1px solid rgba(255,255,255,0.24);
  border-radius: 16px;
  padding: 12px 16px;
  min-width: 220px;
  text-align: center;
  box-shadow: inset 0 1px 0 rgba(255,255,255,0.10);
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
}

    .summary-badge .label {
      display: block;
      font-size: 11px;
      color: rgba(255,255,255,0.75);
      margin-bottom: 4px;
    }

    .summary-badge .value {
      font-size: 24px;
      font-weight: 800;
      letter-spacing: -0.03em;
      color: #fff;
    }

    .party-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 14px;
      margin: 22px 0 24px;
    }

    .card {
      background: linear-gradient(180deg, #ffffff, #fcfaff);
      border: 1px solid var(--line-strong);
      border-radius: 20px;
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
      color: #2e1065;
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
      color: #2e1065;
    }

    .section-head p {
      margin: 0;
      color: var(--muted);
      font-size: 13px;
    }

    .rich-text {
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      background: #fff;
      padding: 18px;
    }

    .rich-text h1,
    .rich-text h2,
    .rich-text h3,
    .rich-text h4 {
      color: #2e1065;
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
      border-radius: 16px;
      padding: 14px;
      background: #fcfaff;
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
      color: #2e1065;
    }

    .contract-pos__price {
      white-space: nowrap;
      font-size: 13px;
      font-weight: 800;
      color: #5b21b6;
    }

    .contract-pos section {
      margin-top: 12px;
    }

    .contract-pos section h4 {
      margin: 0 0 6px;
      font-size: 13px;
      color: #6b21a8;
    }

    .price-box {
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      background: linear-gradient(180deg, #ffffff, #faf5ff);
      padding: 16px;
      margin-top: 18px;
    }

    .price-box h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #2e1065;
    }

    .price-box table {
      width: 100%;
      border-collapse: collapse;
    }

    .price-box th,
    .price-box td {
      padding: 10px 8px;
      border-bottom: 1px dashed #e9d5ff;
      font-size: 14px;
      color: #26203a;
      vertical-align: top;
    }

    .price-box th {
      text-align: left;
      color: #6b21a8;
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
      border-radius: 18px;
      padding: 16px;
      background: #fff;
    }

    .contract-note h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #2e1065;
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
      border-radius: 18px;
      padding: 16px;
      background: #fcfaff;
      page-break-inside: avoid;
      break-inside: avoid;
    }

    .signature-placeholder h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #2e1065;
    }

    .signature-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .signature-box {
      border-top: 1px solid #7c3aed;
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
  @page {
    size: A4;
    margin: 16mm 12mm 16mm 12mm;
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

  .top-bar {
    margin: 0 0 9mm 0 !important;
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
    border: 1px solid #d7c7f3 !important;
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
    border-bottom: 1px dashed #d7c7f3 !important;
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

          <div class="price-box">
            <h3>Preisübersicht</h3>
            {{m.PriceBoxHtml}}
          </div>
        </div>
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
    }
}