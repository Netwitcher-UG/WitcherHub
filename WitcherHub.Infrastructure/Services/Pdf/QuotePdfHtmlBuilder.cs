using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    public static class QuotePdfHtmlBuilder
    {
        public sealed class QuotePdfCustomer
        {
            public string DisplayName { get; init; } = "";
            public string? CompanyName { get; init; }
            public string? Street { get; init; }
            public string? PostalCode { get; init; }
            public string? City { get; init; }
            public string? Country { get; init; }
            public string? Email { get; init; }
        }

        public sealed class QuotePdfLine
        {
            public int Position { get; init; }

            public string Title { get; init; } = "";
            public string? Description { get; init; }

            public decimal Quantity { get; init; }
            public decimal UnitPrice { get; init; }

            public string BillingCycleText { get; init; } = "";

            public decimal VatPercent { get; init; }

            public string DiscountDisplay { get; init; } = "—";

            public decimal SubTotal { get; init; }
            public decimal Discount { get; init; }
            public decimal Tax { get; init; }
            public decimal Total { get; init; }
        }

        public sealed class QuotePdfTotals
        {
            public decimal SubTotal { get; init; }
            public decimal Discount { get; init; }
            public decimal Tax { get; init; }
            public decimal Total { get; init; }
            public decimal VatPercent { get; init; } = 19m;
        }

        public sealed class QuotePdfDocumentModel
        {
            public Guid QuoteId { get; init; }
            public Guid ProjectId { get; init; }
            public string QuoteNo { get; init; } = "";
            public string Currency { get; init; } = "EUR";
            public string StatusText { get; init; } = "";
            public DateTimeOffset CreatedAt { get; init; }
            public DateTimeOffset? IssuedAt { get; init; }
            public DateTimeOffset? ExpiresAt { get; init; }
            public string? Notes { get; init; }
            public string ProjectTitle { get; init; } = "";
            

            public QuotePdfCustomer Customer { get; init; } = new();
            public List<QuotePdfLine> Lines { get; init; } = new();
            public QuotePdfTotals Totals { get; init; } = new();

            // ✅ Company info (as in your reference PDF)
            public string CompanyName { get; init; } = "Netwitcher UG (haftungsbeschränkt)";
            public string CompanyLine1 { get; init; } = "Kochhannstraße 6";
            public string CompanyLine2 { get; init; } = "10429 Berlin";
            public string CompanyLine3 { get; init; } = "017673247186";
            public string CompanyEmail { get; init; } = "info@netwitcher.com";
        }

        public static string Build(QuotePdfDocumentModel m)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            string Money(decimal v)
            {
                return (m.Currency ?? "EUR").ToUpperInvariant() == "EUR"
                    ? v.ToString("N2", de) + " €"
                    : v.ToString("N2", de) + " " + (m.Currency ?? "");
            }

            string D(DateTimeOffset? d) => d.HasValue ? d.Value.ToString("dd.MM.yyyy", de) : "—";
            string E(string? s) => WebUtility.HtmlEncode(s ?? "");

            string BuildBadgeText(string? text)
            {
                var parts = (text ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length == 0)
                    return "WH";

                if (parts.Length == 1)
                {
                    var one = parts[0];
                    return (one.Length >= 2 ? one[..2] : one).ToUpperInvariant();
                }

                return string.Concat(parts.Take(2).Select(x => x[0])).ToUpperInvariant();
            }

            var customerTitle = !string.IsNullOrWhiteSpace(m.Customer.CompanyName)
                ? m.Customer.CompanyName!
                : m.Customer.DisplayName;

            var headlineTitle = "Angebot";
            var summaryTitle = !string.IsNullOrWhiteSpace(m.ProjectTitle) ? m.ProjectTitle : "Leistungsangebot";
            var summaryText = "Dieses Angebot enthält die vereinbarten Leistungen und Preise.";
            var notesText = !string.IsNullOrWhiteSpace(m.Notes)
                ? m.Notes!
                : "Dieses Angebot basiert auf dem derzeit vereinbarten Leistungsumfang. Zusätzliche Leistungen oder nachträgliche Erweiterungen können separat angeboten und berechnet werden.";

            var hasDiscount = m.Totals.Discount > 0m;
            var hasVat = m.Totals.Tax > 0m && m.Totals.VatPercent > 0m;
            

            var providerInfo = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(m.CompanyLine1))
                providerInfo.AppendLine($@"<div>{E(m.CompanyLine1)}</div>");
            if (!string.IsNullOrWhiteSpace(m.CompanyLine2))
                providerInfo.AppendLine($@"<div>{E(m.CompanyLine2)}</div>");
            if (!string.IsNullOrWhiteSpace(m.CompanyLine3))
                providerInfo.AppendLine($@"<div>{E(m.CompanyLine3)}</div>");
            if (!string.IsNullOrWhiteSpace(m.CompanyEmail))
                providerInfo.AppendLine($@"<div>{E(m.CompanyEmail)}</div>");

            var customerInfo = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(m.Customer.Street))
                customerInfo.AppendLine($@"<div>{E(m.Customer.Street)}</div>");

            if (!string.IsNullOrWhiteSpace(m.Customer.PostalCode) || !string.IsNullOrWhiteSpace(m.Customer.City))
            {
                var cityLine = ((m.Customer.PostalCode ?? "").Trim() + " " + (m.Customer.City ?? "").Trim()).Trim();
                customerInfo.AppendLine($@"<div>{E(cityLine)}</div>");
            }

            if (!string.IsNullOrWhiteSpace(m.Customer.Country))
                customerInfo.AppendLine($@"<div>{E(m.Customer.Country)}</div>");

            if (!string.IsNullOrWhiteSpace(m.Customer.Email))
                customerInfo.AppendLine($@"<div>{E(m.Customer.Email)}</div>");
            
            var rows = new StringBuilder();

            foreach (var l in (m.Lines ?? new()).OrderBy(x => x.Position))
            {
                var descriptionHtml = !string.IsNullOrWhiteSpace(l.Description)
                    ? $@"<div class=""service-note"">{E(l.Description)}</div>"
                    : @"<div class=""desc-empty"">—</div>";

                rows.Append($$"""
<tr>
  <td class="center">{{l.Position}}</td>
  <td>
    <div class="service-title">{{E(l.Title)}}</div>
  </td>
  <td>
    {{descriptionHtml}}
  </td>
  <td>{{E(l.BillingCycleText)}}</td>
  <td class="center">{{E(l.Quantity.ToString("0.##", de))}}</td>
  <td class="right">{{E(Money(l.UnitPrice))}}</td>
  <td class="center">{{E(l.VatPercent.ToString("0.##", de))}}</td>
  <td class="center">{{E(l.DiscountDisplay)}}</td>
  <td class="right"><strong>{{E(Money(l.Total))}}</strong></td>
</tr>

""");
            }

            var discountLine = hasDiscount
                ? $$"""
<div class="total-line">
  <span>Rabatt</span>
  <strong>-{{E(Money(m.Totals.Discount))}}</strong>
</div>
"""
                : string.Empty;

            var vatLine = hasVat
                ? $$"""
<div class="total-line">
  <span>zzgl. MwSt. ({{E(m.Totals.VatPercent.ToString("0.##", de))}}%)</span>
  <strong>{{E(Money(m.Totals.Tax))}}</strong>
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
      font-family: "Inter", "Segoe UI", Tahoma, Arial, sans-serif;
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

    .header {
      display: flex;
      justify-content: space-between;
      gap: 18px;
      align-items: flex-start;
      flex-wrap: nowrap;
      margin-bottom: 20px;
    }

    .header-left {
      display: flex;
      align-items: flex-start;
      gap: 16px;
      flex: 1 1 auto;
      min-width: 0;
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

    .meta {
      display: grid;
      gap: 8px;
      min-width: 255px;
      max-width: 255px;
      padding: 14px 16px;
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      background: linear-gradient(180deg, #ffffff, #fcfaff);
      flex-shrink: 0;
    }

    .meta-row {
      display: flex;
      justify-content: space-between;
      gap: 14px;
      font-size: 13px;
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

    .chip-row {
  display: flex;
  flex-wrap: nowrap;
  gap: 6px;
  margin-top: 14px;
  white-space: nowrap;
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
      max-width: 480px;
      font-size: 13px;
    }

    .summary-badge {
      background: rgba(255,255,255,0.14);
      border: 1px solid rgba(255,255,255,0.24);
      border-radius: 16px;
      padding: 12px 16px;
      min-width: 190px;
      text-align: right;
      box-shadow: inset 0 1px 0 rgba(255,255,255,0.10);
      flex-shrink: 0;
    }

    .summary-badge .label {
      display: block;
      font-size: 11px;
      color: rgba(255,255,255,0.75);
      margin-bottom: 4px;
    }

    .summary-badge .value {
      font-size: 26px;
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
      color: #4b5563;
      font-size: 13px;
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

    .table-wrap {
      overflow: hidden;
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      background: #fff;
    }

    table {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
  page-break-inside: auto;
}
thead {
  display: table-header-group;
}

tfoot {
  display: table-footer-group;
}

tr {
  page-break-inside: avoid;
  break-inside: avoid;
  page-break-after: auto;
}
    thead th {
  background: #faf5ff;
  color: #6b21a8;
  font-size: 12px;
  font-weight: 800;
  text-align: left;
  padding: 12px 10px;
  border-right: 1px solid var(--line-strong);
  border-bottom: 1px solid var(--line-strong);
  white-space: normal;
  line-height: 1.25;
  word-break: break-word;
  overflow-wrap: anywhere;
}

    thead th:last-child {
      border-right: 0;
    }

    tbody td {
      padding: 12px 8px;
      border-right: 1px solid var(--line-strong);
      border-bottom: 1px solid var(--line-strong);
      vertical-align: top;
      font-size: 12px;
      color: #31263f;
      word-break: break-word;
      overflow-wrap: anywhere;
    }

    tbody td:last-child {
      border-right: 0;
    }

    tbody tr:last-child td {
      border-bottom: 0;
    }

    .service-title {
      font-weight: 800;
      font-size: 13px;
      line-height: 1.4;
      color: #2e1065;
      margin-bottom: 3px;
    }

    .service-note {
      color: var(--muted);
      font-size: 12px;
      line-height: 1.45;
      white-space: pre-wrap;
      word-break: break-word;
    }

    .desc-empty {
      color: #9b8ab6;
      font-size: 12px;
      line-height: 1.45;
      font-style: italic;
    }

    .center {
      text-align: center;
      font-variant-numeric: tabular-nums;
    }

    .right {
      text-align: right;
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
    }

    .layout-bottom {
  display: grid;
  grid-template-columns: 1.15fr 0.85fr;
  gap: 16px;
  margin-top: 18px;
  align-items: start;
  page-break-inside: avoid;
  break-inside: avoid;
}

    .notes {
  page-break-inside: avoid;
break-inside: avoid;
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      padding: 16px;
      background: #fff;
      min-height: 100%;
    }

    .notes h3,
    .totals h3 {
      margin: 0 0 10px;
      font-size: 16px;
      color: #2e1065;
    }

    .notes p {
      margin: 0;
      color: #5b556a;
      font-size: 13px;
      white-space: pre-wrap;
      word-break: break-word;
      overflow-wrap: anywhere;
    }
    
    .totals {
  page-break-inside: avoid;
break-inside: avoid;
      border: 1px solid var(--line-strong);
      border-radius: 18px;
      padding: 16px;
      background: linear-gradient(180deg, #ffffff, #faf5ff);
    }

    .total-line {
      display: flex;
      justify-content: space-between;
      gap: 14px;
      padding: 9px 0;
      font-size: 13px;
      color: #5b556a;
      border-bottom: 1px dashed #e9d5ff;
    }

    .total-line:last-of-type {
      border-bottom: 0;
    }

    .grand-total {
      display: flex;
      justify-content: space-between;
      gap: 14px;
      margin-top: 12px;
      padding-top: 14px;
      border-top: 2px solid #ddd6fe;
      font-weight: 800;
      font-size: 20px;
      color: #2e1065;
    }

    .w-pos { width: 6%; }
    .w-title { width: 18%; }
    .w-desc { width: 25%; }
    .w-pay { width: 14%; }
    .w-qty { width: 7%; }
    .w-price { width: 10%; }
    .w-tax { width: 8%; }
    .w-discount { width: 10%; }
    .w-total { width: 12%; }

    @media screen and (max-width: 980px) {
      .page {
        width: auto;
        min-height: auto;
        margin: 12px;
      }

      .header,
      .summary-banner,
      .party-grid,
      .layout-bottom {
        display: block;
      }

      .meta,
      .card,
      .notes,
      .totals,
      .summary-badge {
        max-width: 100%;
        width: 100%;
        margin-top: 12px;
      }

      .table-wrap {
        overflow-x: auto;
      }

      table {
        min-width: 980px;
      }
    }

    @media print {
      html, body {
        background: #fff;
      }

      .page {
        width: auto;
        min-height: auto;
        margin: 0;
      }

      .sheet {
        box-shadow: none;
        border: 0;
        border-radius: 0;
      }
      .table-wrap {
  overflow: hidden;
  border: 1px solid var(--line-strong);
  border-radius: 18px;
  background: #fff;
}
      .content {
        padding: 0;
      }

      .card,
      .table-wrap,
      .notes,
      .totals,
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
              <h1>{{E(headlineTitle)}}</h1>
              

              <div class="chip-row">
                <div class="chip project">Status: <strong>{{E(m.StatusText)}}</strong></div>
                <div class="chip project">Projekt: <strong>{{E(string.IsNullOrWhiteSpace(m.ProjectTitle) ? "—" : m.ProjectTitle)}}</strong></div>
                <div class="chip validity">Gültig bis: <strong>{{E(D(m.ExpiresAt))}}</strong></div>
              </div>
            </div>
          </div>

          <div class="meta">
            <div class="meta-row">
              <span>Angebotsnummer</span>
              <strong>{{E(m.QuoteNo)}}</strong>
            </div>
            <div class="meta-row">
              <span>Erstellt am</span>
              <strong>{{E(m.CreatedAt.ToString("dd.MM.yyyy", de))}}</strong>
            </div>
            <div class="meta-row">
              <span>Ausgestellt am</span>
              <strong>{{E(D(m.IssuedAt))}}</strong>
            </div>
          </div>
        </div>

        <div class="summary-banner">
          <div>
            <h2>{{E(summaryTitle)}}</h2>
            <p>{{E(summaryText)}}</p>
          </div>

          <div class="summary-badge">
            <span class="label">Gesamtbetrag</span>
            <div class="value">{{E(Money(m.Totals.Total))}}</div>
          </div>
        </div>

        <div class="party-grid">
          <div class="card">
            <div class="card-label">Anbieter</div>
            <h3>{{E(m.CompanyName)}}</h3>
            <div class="info-list">
              {{providerInfo}}
            </div>
          </div>

          <div class="card">
            <div class="card-label">Kunde</div>
            <h3>{{E(customerTitle)}}</h3>
            <div class="info-list">
              {{customerInfo}}
            </div>
          </div>
        </div>

        <div class="section">
          <div class="section-head">
            <div>
              <h2>Leistungspositionen</h2>
              <p>Alle Positionen und Details sind übersichtlich in Tabellenform dargestellt.</p>
            </div>
          </div>

          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th class="w-pos">Pos.</th>
                  <th class="w-title">Bezeichnung</th>
                  <th class="w-desc">Beschreibung</th>
                  <th class="w-pay">Zahlungs-<br>weise</th>
                  <th class="w-qty center">Menge</th>
                  <th class="w-price right">Einzel €</th>
                  <th class="w-tax center">USt. %</th>
                  <th class="w-discount center">Rabatt</th>
                  <th class="w-total right">Gesamt €</th>
                </tr>
              </thead>
              <tbody>
                {{rows}}
              </tbody>
            </table>
          </div>

          <div class="layout-bottom">
            <div class="notes">
              <h3>Hinweise</h3>
              <p>{{E(string.IsNullOrWhiteSpace(m.Notes) ? "-" : m.Notes)}}</p>
            </div>

            <div class="totals">
              <h3>Finanzübersicht</h3>

              <div class="total-line">
                <span>Zwischensumme</span>
                <strong>{{E(Money(m.Totals.SubTotal))}}</strong>
              </div>

              {{discountLine}}

              {{vatLine}}

              <div class="grand-total">
                <span>Gesamt</span>
                <span>{{E(Money(m.Totals.Total))}}</span>
              </div>
            </div>
          </div>
        </div>

      </div>
    </div>
  </div>
</body>
</html>
""";
        }
        
        }



    }
