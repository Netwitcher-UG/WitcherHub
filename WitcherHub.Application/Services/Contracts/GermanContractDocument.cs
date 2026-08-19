using System.Globalization;
using System.Text;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// Puts generated clauses into the shape a German contract actually has.
    ///
    /// The model was asked for the whole document and produced a web article:
    /// a title like a page heading, sections numbered "1." "2." "3.", and no
    /// party block or signature block at all. Read as data it was correct; as a
    /// document it was wrong, and this one gets printed, sent to a customer and
    /// signed.
    ///
    /// A German contract opens by naming the parties — "zwischen … und …", each
    /// with the role it is called by for the rest of the document — states its
    /// clauses as §§ with numbered (1) paragraphs, and closes with Ort, Datum
    /// and two signature lines. None of that is a matter of wording, so none of
    /// it is left to the model: it is composed here, from the record, the same
    /// way every time. The model writes only the clauses.
    ///
    /// Nothing here invents legal content. There is no Haftung, no Gerichtsstand
    /// and no Schlussbestimmungen clause, because writing those is not a
    /// formatting decision and they are not ours to add.
    /// </summary>
    public static class GermanContractDocument
    {
        private static readonly CultureInfo German = new("de-DE");

        /// <summary>
        /// The two parties, as they are to appear at the head of the contract.
        /// </summary>
        /// <param name="ProviderName">Us, as the Auftragnehmer.</param>
        /// <param name="CustomerName">The customer, as the Auftraggeber.</param>
        public sealed record Parties(
            string? ProviderName,
            string? ProviderAddress,
            string? CustomerName,
            string? CustomerAddress);

        /// <summary>
        /// Composes the document: heading, parties, the given clauses, signatures.
        /// </summary>
        /// <param name="clauses">
        /// The §§ as written by the model. Passed through untouched apart from
        /// trimming — this method decides the document's frame, not its wording.
        /// </param>
        public static string Compose(
            string title,
            string contractNo,
            string? projectTitle,
            Parties parties,
            string clauses,
            DateOnly? start = null,
            DateOnly? end = null)
        {
            var doc = new StringBuilder();

            // The title, centred and spaced by the stylesheet. Upper-cased there
            // too, so the stored text stays readable.
            doc.Append("# ").Append(Clean(title)).Append("\n\n");

            // Vertragsnummer and Projekt directly under it, as one line — the
            // reference by which this document is cited.
            var reference = new List<string> { $"Vertragsnummer: {Clean(contractNo)}" };

            if (!string.IsNullOrWhiteSpace(projectTitle))
                reference.Add($"Projekt: {Clean(projectTitle)}");

            doc.Append(string.Join(" · ", reference)).Append("\n\n");

            doc.Append(PartyBlock(parties));

            // Laufzeit belongs with the parties rather than buried in a clause:
            // it is the first thing a reader checks. Stated only when known —
            // an invented term would change what the contract says.
            if (start is not null || end is not null)
            {
                doc.Append("<p class=\"vertrag-laufzeit\">")
                   .Append(Laufzeit(start, end))
                   .Append("</p>\n\n");
            }

            doc.Append("<p class=\"vertrag-konjunktion\">wird der folgende Vertrag geschlossen:</p>\n\n");

            doc.Append(clauses.Trim()).Append("\n\n");

            doc.Append(SignatureBlock(parties));

            return doc.ToString().TrimEnd() + "\n";
        }

        /// <summary>
        /// „zwischen … und …", each party followed by the name it is called by
        /// for the rest of the document.
        /// </summary>
        private static string PartyBlock(Parties parties)
        {
            var block = new StringBuilder();

            block.Append("<div class=\"vertrag-parteien\">\n\n");
            block.Append("<p class=\"vertrag-konjunktion\">zwischen</p>\n\n");

            block.Append(Party(
                parties.ProviderName ?? "Auftragnehmer",
                parties.ProviderAddress,
                "Auftragnehmer"));

            block.Append("<p class=\"vertrag-konjunktion\">und</p>\n\n");

            block.Append(Party(
                parties.CustomerName ?? "Auftraggeber",
                parties.CustomerAddress,
                "Auftraggeber"));

            block.Append("</div>\n\n");

            return block.ToString();
        }

        private static string Party(string name, string? address, string role)
        {
            var lines = new StringBuilder();

            lines.Append("<div class=\"vertrag-partei\">\n");
            lines.Append("<strong>").Append(Clean(name)).Append("</strong>\n");

            if (!string.IsNullOrWhiteSpace(address))
            {
                // Each address line stays its own line: an address reflowed into
                // a paragraph is not an address.
                foreach (var line in address.Replace("\r\n", "\n").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Append(Clean(line.Trim())).Append("<br />\n");
                }
            }

            // The German convention, with the typographic quotes it is set in.
            lines.Append("<span class=\"vertrag-rolle\">– nachfolgend „")
                 .Append(role)
                 .Append("“ genannt –</span>\n");

            lines.Append("</div>\n\n");

            return lines.ToString();
        }

        /// <summary>
        /// The term, in the form a German contract states it. An open-ended
        /// contract says so rather than leaving the reader to infer it from a
        /// missing date.
        /// </summary>
        internal static string Laufzeit(DateOnly? start, DateOnly? end)
        {
            var from = start?.ToString("dd.MM.yyyy", German);
            var to = end?.ToString("dd.MM.yyyy", German);

            return (from, to) switch
            {
                (not null, not null) => $"Laufzeit: {from} bis {to}",
                (not null, null) => $"Laufzeit: ab {from}, auf unbestimmte Zeit",
                (null, not null) => $"Laufzeit: bis {to}",
                _ => ""
            };
        }

        /// <summary>
        /// Ort, Datum and two signature lines — the part that makes the printed
        /// document signable, and which the model produced no version of at all.
        /// </summary>
        private static string SignatureBlock(Parties parties)
        {
            var block = new StringBuilder();

            block.Append("<div class=\"vertrag-unterschriften\">\n\n");

            block.Append(Signature(parties.ProviderName, "Auftragnehmer"));
            block.Append(Signature(parties.CustomerName, "Auftraggeber"));

            block.Append("</div>\n");

            return block.ToString();
        }

        private static string Signature(string? name, string role) =>
            "<div class=\"vertrag-unterschrift\">\n" +
            "<p class=\"vertrag-ort-datum\">Ort, Datum</p>\n" +
            "<div class=\"vertrag-unterschrift-linie\"></div>\n" +
            "<p class=\"vertrag-unterschrift-name\">" +
            Clean(name ?? role) + "<br />" + role +
            "</p>\n" +
            "</div>\n\n";

        /// <summary>
        /// Keeps composed text from breaking the document it is placed into.
        ///
        /// These values come from the customer record, where somebody may well
        /// have typed a &lt; or an &amp;. Left raw they would be read as markup
        /// by the Markdown renderer.
        /// </summary>
        private static string Clean(string value) =>
            (value ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Trim();
    }
}
