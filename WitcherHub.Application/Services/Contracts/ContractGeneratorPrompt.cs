using System.Text;
using System.Text.Json;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// The one place contract-generation behaviour is controlled.
    ///
    /// There were two prompts. One asked for free-form Markdown of the whole
    /// document and was what the Generate button used; the other — in
    /// ContractDocumentGenerator, still registered and still called by other
    /// pages — asked for strict JSON with a schema and a list of hard rules, and
    /// then rendered it here rather than trusting the model with layout. The
    /// second was the better design and the draft pipeline never touched it.
    ///
    /// Its useful rules are carried over: JSON only, German only, no legal terms
    /// invented, no prices invented, no acceptance deadlines. What is new is that
    /// the model is given the parties, the project, the contract details, the
    /// confirmed terms and the optional source text, and is told in what order
    /// they count — so it can write a whole contract instead of a fragment
    /// somebody then had to staple to a pasted document.
    ///
    /// It returns sections, not styling. Fonts, spacing, § numbering, the party
    /// block and the signature block are WitcherHub's, so that every contract
    /// looks like the same company's contract.
    /// </summary>
    public static class ContractGeneratorPrompt
    {
        /// <summary>
        /// Identifies which generation logic produced a version. Bumped when the
        /// rules or the schema change in a way that would make two versions
        /// incomparable.
        ///
        /// v1 was free-form Markdown of the whole document, including whatever
        /// frame the model felt like writing.
        /// </summary>
        public const string Version = "contract-generator-v2";

        /// <summary>
        /// The system instruction: what the model is, and what it must never do.
        /// Separate from the request so it is the same on every call.
        /// </summary>
        public static string SystemInstruction =>
            """
            You draft the clause content of German business contracts for an agency.

            You return structured JSON only. You never return Markdown, HTML,
            styling, fonts, spacing or layout of any kind: the application owns
            how the contract looks, and every contract it produces must look the
            same. You provide only what the clauses say.

            You never invent a figure, a date, a party or a service. You never
            write legal terms — liability, warranty, jurisdiction, data
            protection, termination rights, statutory references or final
            provisions — because those are not drafted by you. If information is
            missing you say so in German with "wird noch festgelegt" rather than
            supplying a plausible value.
            """;

        /// <summary>
        /// The request for one contract, built from the assembled context.
        /// </summary>
        public static string Build(ContractGenerationContext context)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine($"Draft the clauses of a German service contract in {context.Language}.");
            prompt.AppendLine();

            // ---- the schema ------------------------------------------------
            prompt.AppendLine("Return JSON only. No markdown, no code fences, no commentary.");
            prompt.AppendLine();
            prompt.AppendLine("Schema (no extra properties):");
            prompt.AppendLine("""
                {
                  "language": "de",
                  "contractType": "Dienstleistungsvertrag",
                  "preamble": "string or null",
                  "sections": [
                    {
                      "heading": "Gegenstand des Vertrags",
                      "paragraphs": ["string", "string"],
                      "items": ["string"]
                    }
                  ]
                }
                """);
            prompt.AppendLine();
            prompt.AppendLine("""
                - "sections" are the §§ in order. Do not number the headings: the
                  application numbers them. Write "Vergütung und Zahlung", not
                  "§ 4 Vergütung und Zahlung".
                - "paragraphs" are the numbered paragraphs of that §, in order,
                  without their numbers — the application adds (1), (2), (3).
                - "items" is an optional lettered list belonging to the last
                  paragraph, without the a) b) c) markers.
                - "preamble" is one short Präambel or null. Do not put the party
                  block, the term or a signature block anywhere: the application
                  composes those from its own records.
                """);
            prompt.AppendLine();

            // ---- the sections to produce -----------------------------------
            prompt.AppendLine("Produce these sections, in this order, omitting any for which there is nothing to say:");
            prompt.AppendLine("""
                1. Gegenstand des Vertrags — what is being delivered.
                2. Leistungsumfang — one paragraph per position, from its scope and deliverables.
                3. Mitwirkungspflichten des Auftraggebers — what the customer must provide.
                4. Abnahme — measurable checks only (completeness, format, consistency).
                5. Annahmen und Ausschlüsse — assumptions, and what is out of scope.
                6. Vergütung und Zahlung — restate the figures below exactly.
                7. Laufzeit und Aktivierung — billing cycle, duration, activation.
                """);
            prompt.AppendLine();

            // ---- the rules, carried over from the structured generator -----
            prompt.AppendLine("Rules:");
            prompt.AppendLine("""
                - German throughout, formal register: "Der Auftragnehmer erbringt …",
                  never "Wir machen …".
                - Refer to the parties only as "der Auftragnehmer" (us) and "der
                  Auftraggeber" (the customer). Never write either company name into
                  a clause; the application names them at the head of the document.
                - German number format: 1.234,56 EUR. German dates: 31.03.2027.
                - Restate every figure exactly as given. Do not recalculate, round,
                  convert, or introduce a figure that is not in the data below.
                - Do not write liability, warranty, jurisdiction, data protection,
                  termination rights or final provisions. Do not cite laws or §§ of
                  legislation.
                - Do not set acceptance deadlines in days, weeks or months.
                - One line per list item, no line breaks inside an item.
                - Where information is missing, write "wird noch festgelegt".
                """);
            prompt.AppendLine();

            // ---- precedence, stated to the model ---------------------------
            prompt.AppendLine("Where two sources disagree, the earlier in this list wins:");

            for (var i = 0; i < ContractGenerationContext.Precedence.Count; i++)
                prompt.AppendLine($"  {i + 1}. {ContractGenerationContext.Precedence[i]}");

            prompt.AppendLine();

            // ---- the data --------------------------------------------------
            prompt.AppendLine("=== AUTHORITATIVE DATA ===");
            prompt.AppendLine(Json(new
            {
                provider = context.Provider,
                customer = context.Customer,
                project = context.Project,
                contract = context.Contract,
                totals = context.Totals,
                confirmedTerms = context.ConfirmedTerms,
                positions = context.Positions.Select(p => new
                {
                    position = p.Position,
                    title = p.Title,
                    serviceType = p.ServiceType,
                    description = p.Description,
                    scope = p.Scope,
                    deliverables = p.Deliverables,
                    quantity = p.Quantity,
                    unit = p.Unit,
                    pricingModel = p.PricingModel.ToString(),
                    unitPrice = p.UnitPrice,
                    currency = p.Currency,
                    vatRate = p.VatRate,
                    discountType = p.DiscountType?.ToString(),
                    discountValue = p.DiscountValue,
                    netTotal = p.NetTotal,
                    billingCycle = p.BillingCycle.ToString(),
                    durationPeriods = p.DurationPeriods,
                    deliveryMethod = p.DeliveryMethod,
                    activationMethod = p.ActivationMethod.ToString(),
                    startDate = p.StartDate?.ToString("yyyy-MM-dd"),
                    deliveryDate = p.DeliveryDate?.ToString("yyyy-MM-dd"),
                    isFree = p.IsFree,
                    acceptanceCriteria = p.AcceptanceCriteria,
                    customerResponsibilities = p.CustomerResponsibilities,
                    assumptions = p.Assumptions,
                    exclusions = p.Exclusions,
                    notes = p.Notes
                })
            }));

            // ---- the source text, last and clearly labelled ----------------
            if (context.HasSourceText)
            {
                prompt.AppendLine();
                prompt.AppendLine("=== SOURCE MATERIAL (CONTEXT ONLY, LOWEST AUTHORITY) ===");
                prompt.AppendLine("""
                    The text below is something the user pasted in: an old agreement, an
                    email, an offer or notes. Use it to understand what kind of contract
                    is wanted and what it should cover.

                    Do not copy it. Do not quote it at length. Do not treat any party,
                    address, figure or date in it as correct where the authoritative data
                    above says otherwise. It is not part of the contract you produce.
                    """);
                prompt.AppendLine();
                prompt.AppendLine(Truncate(context.SourceText!, 24_000));
            }

            if (!string.IsNullOrWhiteSpace(context.AdditionalInstructions))
            {
                prompt.AppendLine();
                prompt.AppendLine("=== GUIDANCE FOR THE WORDING ONLY ===");
                prompt.AppendLine(context.AdditionalInstructions);
            }

            return prompt.ToString();
        }

        private static readonly JsonSerializerOptions Pretty = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static string Json(object value) => JsonSerializer.Serialize(value, Pretty);

        /// <summary>
        /// Keeps a very long pasted document from crowding out the authoritative
        /// data, which is the part that must survive in the request.
        /// </summary>
        private static string Truncate(string text, int max) =>
            text.Length <= max
                ? text
                : text[..max] + "\n\n[… gekürzt …]";
    }
}
