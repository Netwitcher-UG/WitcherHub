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
    /// invented, no prices invented, no acceptance deadlines.
    ///
    /// What changed in v3 is the shape of the job. v2 asked for the whole contract
    /// in one call and named seven sections to produce — so every contract came
    /// back with at most seven short clauses and fitted on one page, whether it
    /// had been given three positions or thirty. Length is not a style choice; it
    /// is what the agreed content requires. So the work is split: the sources are
    /// enumerated into a ledger, a plan is drawn that assigns every ledger entry
    /// to a section, the sections are written a few at a time, and what comes back
    /// is measured against the ledger before it is saved. No stage has a section
    /// cap, and no stage has to fit the whole document into one answer.
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
        /// frame the model felt like writing. v2 was one JSON call for a fixed
        /// list of seven sections. v3 plans against a coverage ledger, writes the
        /// sections in batches, and audits the result.
        /// </summary>
        public const string Version = "contract-generator-v3";

        /// <summary>
        /// How many sections one generation call is asked to write.
        ///
        /// Small on purpose. The reason v2 produced one-page contracts is that a
        /// single answer had to hold the entire document, and a model with a lot
        /// to say and one answer to say it in writes less about everything. Four
        /// sections at a time means a thirty-section contract is thirty sections
        /// long rather than seven.
        /// </summary>
        public const int SectionsPerCall = 4;

        /// <summary>
        /// The system instruction: what the model is, and what it must never do.
        /// Separate from the request so it is the same on every call.
        ///
        /// This was written for v2 and never sent: <c>IAiTextGenerator</c> took a
        /// single prompt string and had nowhere to put it. It is sent now.
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

            The document is as long as its content requires. You never shorten a
            contract to make it fit, and you never pad one to make it look
            substantial. Every agreed item gets the words it needs and no more.
            """;

        // ================================================== stage 1: the source

        /// <summary>
        /// Reads a pasted document for what it establishes, before any contract is
        /// planned.
        ///
        /// This is the only stage that has to read free prose, and it is kept
        /// separate so that what was found is a list somebody can look at rather
        /// than an impression that disappears into a draft. The ids are assigned
        /// by the application afterwards; the model is not asked to number
        /// anything, because a model that renumbers between calls makes the audit
        /// meaningless.
        /// </summary>
        public static string SourceAnalysis(ContractGenerationContext context)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine("Read the document below and list everything it establishes.");
            prompt.AppendLine();
            prompt.AppendLine("Return JSON only. No markdown, no code fences, no commentary.");
            prompt.AppendLine();
            prompt.AppendLine("""
                {
                  "topics": [
                    { "topic": "short German label", "detail": "one sentence in German" }
                  ]
                }
                """);
            prompt.AppendLine();
            prompt.AppendLine("""
                - One entry per distinct subject: a service promised, an obligation
                  placed on either side, a condition, an arrangement, a restriction,
                  a process, a dependency, a thing explicitly excluded.
                - Be exhaustive. A long document has many entries. Do not summarise
                  several subjects into one entry, and do not stop early.
                - Do not copy figures, prices, dates, party names or addresses into
                  "detail". Those come from this system's own records, not from this
                  document, and repeating them here would put a stale number into a
                  new contract. Write "Preis vereinbart" rather than the price.
                - Do not invent. If the document does not establish something, it is
                  not in the list.
                - Leave out anything that is only formatting, letterhead, signature
                  blocks or page furniture.
                """);
            prompt.AppendLine();
            prompt.AppendLine("=== DOCUMENT ===");
            prompt.AppendLine(Truncate(context.SourceText ?? "", SourceTextBudget));

            return prompt.ToString();
        }

        // ================================================== stage 2: the plan

        /// <summary>
        /// Plans the contract: which §§ it has, in what order, and which ledger
        /// entry each one is responsible for.
        ///
        /// The plan is what replaced the fixed list of seven headings. It has no
        /// cap and no minimum: a contract covering four ledger entries gets a few
        /// sections, one covering sixty gets many, and neither is padded or cut to
        /// reach a page count.
        /// </summary>
        public static string Outline(ContractGenerationContext context, ContractCoverageLedger ledger)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine($"Plan the sections of a German service contract in {context.Language}.");
            prompt.AppendLine();
            prompt.AppendLine("Return JSON only. No markdown, no code fences, no commentary.");
            prompt.AppendLine();
            prompt.AppendLine("""
                {
                  "contractType": "Dienstleistungsvertrag",
                  "preamble": "one short Präambel in German, or null",
                  "sections": [
                    {
                      "heading": "Gegenstand des Vertrags",
                      "intent": "what this § has to establish, one sentence",
                      "covers": ["POS-001-01", "REC-001"]
                    }
                  ]
                }
                """);
            prompt.AppendLine();
            prompt.AppendLine("""
                Planning rules:
                - Every id in the coverage list below must appear in exactly one
                  section's "covers". Nothing may be left unassigned, and nothing
                  may be assigned twice.
                - There is no minimum and no maximum number of sections. The
                  contract is as long as its content. Do not aim for a page count,
                  do not aim for a section count, and do not merge unrelated
                  subjects to make the document shorter.
                - Group by subject, not by source. Two positions with the same kind
                  of obligation belong together; one position's price and its scope
                  belong apart.
                - A section that would carry more than about eight ids is doing too
                  much: split it, and give each part its own heading.
                - Order the sections as a German contract runs: what is being
                  delivered, how, what each side must do, what is assumed and
                  excluded, what it costs, when it is paid, how long it runs.
                - Do not number the headings: the application numbers them. Write
                  "Vergütung und Zahlung", not "§ 4 Vergütung und Zahlung".
                - Do not plan a section for the party block, the term summary, the
                  signatures or the annexes. The application composes those.
                - Do not plan a liability, warranty, jurisdiction, data protection,
                  termination or final-provisions section. Those are not drafted
                  here.
                """);
            prompt.AppendLine();

            AppendPrecedence(prompt);
            AppendAuthoritativeData(prompt, context);
            AppendLedger(prompt, ledger);
            AppendGuidance(prompt, context);

            return prompt.ToString();
        }

        // ================================================== stage 3: the clauses

        /// <summary>
        /// Writes a few planned sections in full.
        ///
        /// Given the ledger entries the plan assigned to them, and nothing else to
        /// cover, so the model spends its answer on these rather than rationing it
        /// across a whole contract.
        /// </summary>
        public static string Sections(
            ContractGenerationContext context,
            ContractCoverageLedger ledger,
            IReadOnlyList<ContractOutline.PlannedSection> batch)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine($"Write the following sections of a German service contract in {context.Language}.");
            prompt.AppendLine();
            prompt.AppendLine("Return JSON only. No markdown, no code fences, no commentary.");
            prompt.AppendLine();
            prompt.AppendLine("Schema (no extra properties):");
            prompt.AppendLine("""
                {
                  "sections": [
                    {
                      "heading": "Gegenstand des Vertrags",
                      "paragraphs": ["string", "string"],
                      "items": ["string"],
                      "covers": ["POS-001-01"]
                    }
                  ]
                }
                """);
            prompt.AppendLine();
            prompt.AppendLine("""
                - Return one entry per requested section, with the heading exactly as
                  given. Do not add sections, do not drop sections, do not reorder.
                - "paragraphs" are the numbered paragraphs of that §, in order,
                  without their numbers — the application adds (1), (2), (3).
                - "items" is an optional lettered list belonging to the last
                  paragraph, without the a) b) c) markers.
                - "covers" repeats the ids this section actually accounts for. List
                  an id only if the text you wrote genuinely states it.
                - Write every assigned entry into the text. An entry that is only
                  alluded to is not covered. A list of eight deliverables is eight
                  list items, not "verschiedene Leistungen".
                - Use the words the entry gives you. Do not compress an entry into a
                  category name, and do not drop the detail because another section
                  mentions the same position.
                """);
            prompt.AppendLine();

            AppendRules(prompt);
            AppendPrecedence(prompt);

            // The parties, the project, the term and the totals — not the full
            // position list.
            //
            // Every section call used to carry the whole structured record, so a
            // contract with thirty positions sent all thirty of them three or four
            // times over. That is a large input on every call, it is paid for
            // every time, and it is the part of the prompt the model does not need
            // here: the entries this batch must cover are listed below in full,
            // and they came from those same positions.
            AppendFrameData(prompt, context);

            prompt.AppendLine();
            prompt.AppendLine("=== SECTIONS TO WRITE ===");

            var assigned = new List<CoverageItem>();

            foreach (var section in batch)
            {
                prompt.AppendLine();
                prompt.AppendLine($"HEADING: {section.Heading}");

                if (!string.IsNullOrWhiteSpace(section.Intent))
                    prompt.AppendLine($"PURPOSE: {section.Intent}");

                prompt.AppendLine("MUST COVER:");

                foreach (var id in section.Covers)
                {
                    if (ledger[id] is not { } item) continue;
                    assigned.Add(item);
                    prompt.AppendLine($"  {item.Id} [{item.Topic}] {Flatten(item.Detail)}");
                }
            }

            if (assigned.Any(i => i.IsCommercial))
            {
                prompt.AppendLine();
                prompt.AppendLine(
                    "Entries above marked with a figure or a date must be restated exactly as written. " +
                    "Do not round, recalculate, convert or reword them.");
            }

            AppendGuidance(prompt, context);

            return prompt.ToString();
        }

        // ================================================== stage 4: the repair

        /// <summary>
        /// One targeted pass over what the audit found missing.
        ///
        /// Only the sections with gaps are rewritten, and only the missing entries
        /// are named — regenerating the whole contract to fix two clauses is how a
        /// working document gets replaced by a differently-broken one.
        /// </summary>
        public static string Repair(
            ContractGenerationContext context,
            ContractCoverageLedger ledger,
            IReadOnlyList<CoverageGap> gaps,
            IReadOnlyList<string> existingHeadings)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine("The draft contract below is missing things that were agreed. Supply them.");
            prompt.AppendLine();
            prompt.AppendLine("Return JSON only. No markdown, no code fences, no commentary.");
            prompt.AppendLine();
            prompt.AppendLine("""
                {
                  "sections": [
                    {
                      "heading": "Vergütung und Zahlung",
                      "paragraphs": ["string"],
                      "items": ["string"],
                      "covers": ["POS-001-12"]
                    }
                  ]
                }
                """);
            prompt.AppendLine();
            prompt.AppendLine("""
                - Use an existing heading, spelled exactly as listed, to replace that
                  section in full. Its previous text is discarded, so repeat
                  everything that section had to say, not only the missing part.
                - Use a new heading to add a section. Only do that when the missing
                  entry does not belong under any existing one.
                - Cover every entry listed below. That is the whole point of this
                  pass.
                """);
            prompt.AppendLine();

            AppendRules(prompt);

            prompt.AppendLine("=== EXISTING HEADINGS ===");

            foreach (var heading in existingHeadings)
                prompt.AppendLine($"  {heading}");

            prompt.AppendLine();
            prompt.AppendLine("=== ENTRIES THE DRAFT DOES NOT ACCOUNT FOR ===");

            foreach (var gap in gaps)
            {
                var why = gap.Reason switch
                {
                    CoverageGapReason.NotPlanned => "no section covers it",
                    CoverageGapReason.NotWritten => "its section came back empty",
                    _ => "the exact figure or date is not in the text"
                };

                prompt.AppendLine($"  {gap.Item.Id} [{gap.Item.Topic}] ({why}) {Flatten(gap.Item.Detail)}");
            }

            // The frame only. The entries this pass has to supply are listed above
            // with everything they say; the whole position list would be a large
            // input for a call that is already the last thing the user waits on.
            AppendFrameData(prompt, context);
            AppendGuidance(prompt, context);

            return prompt.ToString();
        }

        // ================================================== shared blocks

        /// <summary>
        /// The rules, carried over from the structured generator. Repeated on every
        /// stage that writes clause text, because a rule stated only once, three
        /// calls ago, is a rule the model has stopped applying.
        /// </summary>
        private static void AppendRules(StringBuilder prompt)
        {
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
                - Where information is missing, write "wird noch festgelegt". Never
                  fill a gap with a plausible figure, date or commitment.
                - Do not number the headings: the application numbers them.
                - Do not put the party block, the term summary or a signature block
                  anywhere: the application composes those from its own records.
                """);
            prompt.AppendLine();
        }

        private static void AppendPrecedence(StringBuilder prompt)
        {
            prompt.AppendLine("Where two sources disagree, the earlier in this list wins:");

            for (var i = 0; i < ContractGenerationContext.Precedence.Count; i++)
                prompt.AppendLine($"  {i + 1}. {ContractGenerationContext.Precedence[i]}");

            prompt.AppendLine();
        }

        /// <summary>
        /// The document's frame: who, what project, what term, what it totals.
        ///
        /// What a clause-writing call needs in order to be consistent with the
        /// rest of the contract, without the whole position list it does not.
        /// </summary>
        private static void AppendFrameData(StringBuilder prompt, ContractGenerationContext context)
        {
            prompt.AppendLine("=== AUTHORITATIVE DATA ===");
            prompt.AppendLine(Json(new
            {
                provider = context.Provider,
                customer = context.Customer,
                project = context.Project,
                contract = context.Contract,
                totals = context.Totals,
                confirmedTerms = context.ConfirmedTerms
            }));
        }

        private static void AppendAuthoritativeData(StringBuilder prompt, ContractGenerationContext context)
        {
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
        }

        /// <summary>
        /// The ledger, with the one instruction that makes the ids safe: they are
        /// ours, and they never appear in a contract anybody reads.
        /// </summary>
        private static void AppendLedger(StringBuilder prompt, ContractCoverageLedger ledger)
        {
            prompt.AppendLine();
            prompt.AppendLine("=== COVERAGE LIST ===");
            prompt.AppendLine(
                "Internal references. Never write an id into contract text, a heading, " +
                "a paragraph or a list item — they are for planning only and a customer " +
                "must never see one.");
            prompt.AppendLine();
            prompt.Append(ledger.ToPromptList());
        }

        private static void AppendGuidance(StringBuilder prompt, ContractGenerationContext context)
        {
            if (string.IsNullOrWhiteSpace(context.AdditionalInstructions)) return;

            prompt.AppendLine();
            prompt.AppendLine("=== GUIDANCE FOR THE WORDING ONLY ===");
            prompt.AppendLine(context.AdditionalInstructions);
        }

        // ================================================== helpers

        /// <summary>
        /// How much pasted text the analysis stage reads.
        ///
        /// Raised from the 24.000 characters v2 allowed, because that stage no
        /// longer shares its request with the whole contract's data — reading the
        /// document is all it does, so the document can have the room.
        /// </summary>
        public const int SourceTextBudget = 120_000;

        private static readonly JsonSerializerOptions Pretty = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static string Json(object value) => JsonSerializer.Serialize(value, Pretty);

        private static string Flatten(string text) =>
            text.Replace('\r', ' ').Replace('\n', ' ').Trim();

        /// <summary>
        /// A pasted document longer than this is read up to the cut, and the cut is
        /// stated rather than hidden — a silent truncation reads as a model that
        /// ignored half the document.
        /// </summary>
        internal static string Truncate(string text, int max) =>
            text.Length <= max
                ? text
                : text[..max] + "\n\n[… gekürzt …]";
    }
}
