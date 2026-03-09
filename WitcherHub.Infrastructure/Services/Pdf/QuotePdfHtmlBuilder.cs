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
            public string? ServiceName { get; init; }
            public decimal Quantity { get; init; }
            public decimal UnitPrice { get; init; }
            public decimal SubTotal { get; init; }
            public decimal Discount { get; init; }
            public decimal Tax { get; init; }
            public decimal Total { get; init; }
        }

        public sealed class QuotePdfTotals
        {
            public decimal SubTotal { get; init; }   // net (after discount)
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

            var sb = new StringBuilder(40_000);

            sb.Append(@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <style>
  @page {
    size: A4;
    margin: 18mm 16mm 18mm 16mm;
  }

  * {
    box-sizing: border-box;
  }

  html, body {
    margin: 0;
    padding: 0;
    background: #ffffff;
  }

  body {
    font-family: system-ui, -apple-system, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
    color: #111827;
    font-size: 13px;
    line-height: 1.55;
    font-weight: 400;
    text-rendering: geometricPrecision;
    -webkit-font-smoothing: antialiased;
    font-kerning: normal;
  }

  .page {
    padding: 6px 4px;
  }

  .muted {
    color: #4b5563;
  }

  .small {
    font-size: 11.5px;
    line-height: 1.5;
  }

  .h {
    font-size: 22px;
    font-weight: 800;
    letter-spacing: -.02em;
    margin: 0 0 12px 0;
    color: #0f172a;
  }

  .pillRow {
    margin-top: 14px;
  }

  .pill {
    display: inline-block;
    background: #f8fafc;
    border: 1px solid #e5e7eb;
    border-radius: 999px;
    padding: 8px 12px;
    font-size: 11.5px;
    color: #334155;
    margin: 8px 12px 0 0;
    line-height: 1.2;
  }

  .card {
    border: 1px solid #dbe3ee;
    border-radius: 14px;
    padding: 18px 20px;
    background: #ffffff;
    min-height: 150px;
  }

  .cardTitle {
    font-weight: 800;
    font-size: 14px;
    letter-spacing: -.01em;
    margin: 2px 0 0 0;
    color: #0f172a;
  }

  .cardLabel {
    color: #64748b;
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: .06em;
    margin-bottom: 10px;
  }

  .line {
    margin-top: 7px;
    line-height: 1.65;
  }

  .table {
    width: 100%;
    border-collapse: collapse;
    margin-top: 34px;
    table-layout: auto;
  }

  .table th {
    text-align: left;
    font-size: 11px;
    font-weight: 800;
    text-transform: uppercase;
    letter-spacing: .05em;
    color: #64748b;
    border-bottom: 1px solid #dbe3ee;
    padding: 12px 8px;
    vertical-align: bottom;
  }

  .table td {
    border-bottom: 1px solid #edf2f7;
    padding: 14px 8px;
    vertical-align: top;
    overflow: visible;
  }

  .table th.num,
  .table td.num {
    text-align: right;
    white-space: nowrap;
    overflow: visible;
    font-size: 12px;
    font-variant-numeric: tabular-nums;
    -webkit-font-feature-settings: ""tnum"";
    font-feature-settings: ""tnum"";
    padding-left: 6px;
    padding-right: 6px;
  }

  .table td.num strong {
    white-space: nowrap;
    font-weight: 800;
    color: #0f172a;
  }

  .title {
    font-weight: 800;
    font-size: 13.5px;
    line-height: 1.45;
    color: #0f172a;
  }

  .subline {
    color: #475569;
    font-size: 11.5px;
    margin-top: 5px;
    line-height: 1.6;
  }

  .sumWrap {
    width: 100%;
    margin-top: 32px;
  }

  .sumBox {
    float: right;
    width: 360px;
    border: 1px solid #dbe3ee;
    border-radius: 14px;
    padding: 14px 16px;
    background: #ffffff;
  }

  .sumTbl {
    width: 100%;
    border-collapse: collapse;
  }

  .sumTbl td {
    padding: 9px 0;
    vertical-align: top;
    font-size: 12.5px;
  }

  .sumTbl .r {
    text-align: right;
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
    -webkit-font-feature-settings: ""tnum"";
    font-feature-settings: ""tnum"";
  }

  .sumSep td {
    border-top: 1px solid #dbe3ee;
    padding-top: 13px;
  }

  .sumTotal {
    font-size: 17px;
    font-weight: 900;
    color: #0f172a;
  }

  .clear {
    clear: both;
  }

  strong {
    font-weight: 800;
  }
</style>
</head>
<body>
  <div class=""page"">

    <table width=""100%"" cellspacing=""0"" cellpadding=""0"">
      <tr>
        <td style=""vertical-align:top;"">
          <div class=""h"">Angebot</div>

          <div class=""pillRow"">
            <span class=""pill"">Status: <strong>");
            sb.Append(E(m.StatusText));
            sb.Append(@"</strong></span>
            <span class=""pill"">Projekt: <strong>");
            sb.Append(E(m.ProjectTitle));
            sb.Append(@"</strong></span>
            <span class=""pill"">Gültig bis: <strong>");
            sb.Append(E(D(m.ExpiresAt)));
            sb.Append(@"</strong></span>
          </div>
        </td>

        <td style=""width:210px; vertical-align:top;"" align=""right"">
          <div class=""muted small"" style=""margin-top:28px; line-height:1.7;"">
            Erstellt: <strong>");
            sb.Append(E(m.CreatedAt.ToString("dd.MM.yyyy", de)));
            sb.Append(@"</strong><br/>
            Ausgestellt: <strong>");
            sb.Append(E(D(m.IssuedAt)));
            sb.Append(@"</strong>
          </div>
        </td>
      </tr>
    </table>

    <!-- ✅ Move cards section slightly down -->
    <table width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""margin-top:26px;"">
      <tr>
        <td style=""width:48%; vertical-align:top;"">
          <div class=""card"">
            <div class=""cardLabel"">Von</div>
            <div class=""cardTitle"">");
            sb.Append(E(m.CompanyName));
            sb.Append(@"</div>");

            if (!string.IsNullOrWhiteSpace(m.CompanyLine1)) sb.Append($@"<div class=""muted small line"">{E(m.CompanyLine1)}</div>");
            if (!string.IsNullOrWhiteSpace(m.CompanyLine2)) sb.Append($@"<div class=""muted small line"">{E(m.CompanyLine2)}</div>");
            if (!string.IsNullOrWhiteSpace(m.CompanyLine3)) sb.Append($@"<div class=""muted small line"">{E(m.CompanyLine3)}</div>");
            if (!string.IsNullOrWhiteSpace(m.CompanyEmail)) sb.Append($@"<div class=""muted small line"">{E(m.CompanyEmail)}</div>");

            sb.Append(@"
          </div>
        </td>

        <td style=""width:4%; min-width:32px;""></td>

        <td style=""width:48%; vertical-align:top;"">
          <div class=""card"">
            <div class=""cardLabel"">Angebot für</div>
            <div class=""cardTitle"">");
            sb.Append(E(m.Customer.CompanyName ?? m.Customer.DisplayName));
            sb.Append(@"</div>");

            if (!string.IsNullOrWhiteSpace(m.Customer.Street))
                sb.Append($@"<div class=""muted small line"">{E(m.Customer.Street)}</div>");

            if (!string.IsNullOrWhiteSpace(m.Customer.PostalCode) || !string.IsNullOrWhiteSpace(m.Customer.City))
            {
                var pc = (m.Customer.PostalCode ?? "").Trim();
                var city = (m.Customer.City ?? "").Trim();
                sb.Append($@"<div class=""muted small line"">{E(pc)} {E(city)}</div>");
            }

            if (!string.IsNullOrWhiteSpace(m.Customer.Country))
                sb.Append($@"<div class=""muted small line"">{E(m.Customer.Country)}</div>");

            if (!string.IsNullOrWhiteSpace(m.Customer.Email))
                sb.Append($@"<div class=""muted small line"">{E(m.Customer.Email)}</div>");

            sb.Append(@"
          </div>
        </td>
      </tr>
    </table>
");

            if (!string.IsNullOrWhiteSpace(m.Notes))
            {
                sb.Append(@"
   <div class=""card"" style=""margin-top:18px;"">
      <div class=""cardLabel"">Hinweise</div>
      <div style=""margin-top:10px; white-space:pre-wrap; line-height:1.7;"">");
                sb.Append(E(m.Notes));
                sb.Append(@"</div>
    </div>
");
            }

            sb.Append(@"
    <table class=""table"">
      <thead>
        <tr>
          <th style=""width:60px;"">#</th>
          <th>BESCHREIBUNG</th>
          <th class=""num"" style=""width:90px;"">MENGE</th>
          <th class=""num"" style=""width:170px;"">EINZEL</th>
          <th class=""num"" style=""width:190px;"">BETRAG</th>
        </tr>
      </thead>
      <tbody>
");

            foreach (var l in (m.Lines ?? new()).OrderBy(x => x.Position))
            {
                sb.Append("<tr>");
                sb.Append($@"<td class=""muted"">{l.Position}</td>");

                sb.Append("<td>");
                sb.Append($@"<div class=""title"">{E(l.Title)}</div>");
                if (!string.IsNullOrWhiteSpace(l.ServiceName))
                    sb.Append($@"<div class=""subline"">{E(l.ServiceName)}</div>");

                sb.Append($@"<div class=""subline"">Zwischensumme: <strong>{E(Money(l.SubTotal))}</strong>
 &nbsp;•&nbsp; Rabatt: <strong>-{E(Money(l.Discount))}</strong>
 &nbsp;•&nbsp; MwSt.: <strong>{E(Money(l.Tax))}</strong></div>");
                sb.Append("</td>");

                sb.Append($@"<td class=""num"">{E(l.Quantity.ToString("0.##", CultureInfo.InvariantCulture))}</td>");
                sb.Append($@"<td class=""num"">{E(Money(l.UnitPrice))}</td>");
                sb.Append($@"<td class=""num""><strong>{E(Money(l.Total))}</strong></td>");
                sb.Append("</tr>");
            }

            sb.Append(@"
      </tbody>
    </table>
");

            var grossBeforeDiscount = m.Totals.SubTotal + m.Totals.Discount;

            sb.Append(@"
    <div class=""sumWrap"">
      <div class=""sumBox"">
        <table class=""sumTbl"">
          <tr>
            <td class=""muted"">Zwischensumme</td>
            <td class=""r""><strong>");
            sb.Append(E(Money(grossBeforeDiscount)));
            sb.Append(@"</strong></td>
          </tr>
          <tr>
            <td class=""muted"">Rabatt</td>
            <td class=""r""><strong>-");
            sb.Append(E(Money(m.Totals.Discount)));
            sb.Append(@"</strong></td>
          </tr>
          <tr>
            <td class=""muted"">zzgl. MwSt. (");
            sb.Append(E(m.Totals.VatPercent.ToString("0.##", de)));
            sb.Append(@"%)</td>
            <td class=""r""><strong>");
            sb.Append(E(Money(m.Totals.Tax)));
            sb.Append(@"</strong></td>
          </tr>
          <tr class=""sumSep"">
            <td class=""sumTotal"">Gesamt</td>
            <td class=""r sumTotal"">");
            sb.Append(E(Money(m.Totals.Total)));
            sb.Append(@"</td>
          </tr>
        </table>
      </div>
      <div class=""clear""></div>
    </div>

  </div>
</body>
</html>
");

            return sb.ToString();
        }
    }
}