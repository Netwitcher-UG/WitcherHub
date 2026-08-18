using System.Globalization;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Domain.Commercial;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// Presents a semantic analysis in the shape the review screen already reads.
    ///
    /// The generic pipeline — classify, structure, validate, then calculate — is
    /// what now reads a supplied document. The review screen was written against
    /// the older fixed-field extraction, and rewriting both at once would mean
    /// changing what the analysis says and how it is checked in the same step,
    /// with no way to tell which caused a difference. So the semantic result is
    /// projected into the old shape here, the screen keeps working unchanged, and
    /// the richer result is stored alongside for the screen that replaces it.
    ///
    /// Two rules govern everything below.
    ///
    /// Nothing is invented. A field the document does not state stays empty; it
    /// never acquires a typical value, and an amount the engine could not resolve
    /// is reported as unresolved rather than rounded into the total.
    ///
    /// Nothing is lost. The old shape has a fixed set of fields and the new one
    /// has free-form keys, so a key that matches nothing is appended to
    /// <see cref="ContractExtractionDto.OtherTerms"/> rather than dropped —
    /// silently discarding a term the analyser did read would be worse than
    /// showing it in an imperfect place.
    /// </summary>
    public static class SemanticExtractionProjection
    {
        /// <summary>
        /// Everything below is a reading, not a decision, so every projected value
        /// arrives unconfirmed. A person ticks it in the review screen; until then
        /// it has no effect on what the contract says it costs.
        /// </summary>
        private static ExtractedValue Read(string? value, double confidence = 0.5) =>
            string.IsNullOrWhiteSpace(value)
                ? ExtractedValue.Empty
                : new ExtractedValue
                {
                    Value = value.Trim(),
                    Confidence = Math.Clamp(confidence, 0d, 1d),
                    NeedsConfirmation = true,
                    Confirmed = false
                };

        public static ContractExtractionDto ToLegacyExtraction(SemanticAnalysisResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var extraction = result.Extraction ?? new SemanticExtractionDto();
            var dto = new ContractExtractionDto
            {
                Title = Read(extraction.DocumentTitle),
                ContractType = Read(extraction.DocumentType),
                Purpose = Read(extraction.Purpose),
                Language = Read(extraction.DetectedLanguage)
            };

            var unmatched = new List<string>();

            MapParties(extraction.DetectedParties, dto, unmatched);
            MapContractTerms(extraction.DetectedContractTerms, dto, unmatched);

            dto.Positions = result.Terms.Select(ToPosition).ToList();

            ApplyFinancials(result.Financials, dto);

            CollectWarnings(result, extraction, dto);

            if (unmatched.Count > 0)
            {
                // Appended rather than discarded: these are things the analyser
                // genuinely read out of the document that the old fixed schema has
                // nowhere to put.
                var existing = dto.OtherTerms.HasValue ? dto.OtherTerms.Value + "\n" : "";
                dto.OtherTerms = Read(existing + string.Join("\n", unmatched));
            }

            return dto;
        }

        // ---- parties and contract-level terms ------------------------------

        /// <summary>
        /// The analyser is asked for free-form keys on purpose, so that a document
        /// describing something the schema never anticipated is still recorded.
        /// Matching is therefore tolerant: case, spacing and punctuation are
        /// ignored, and several spellings map to the same field.
        /// </summary>
        private static string Normalise(string key) =>
            new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private static readonly Dictionary<string, string[]> PartyAliases = new()
        {
            ["ProviderName"] = ["providername", "provider", "supplier", "suppliername", "contractor", "agency", "auftragnehmer", "dienstleister"],
            ["ProviderAddress"] = ["provideraddress", "supplieraddress", "contractoraddress", "auftragnehmeradresse"],
            ["ProviderRepresentative"] = ["providerrepresentative", "supplierrepresentative", "providercontact", "vertretenderauftragnehmer"],
            ["CustomerName"] = ["customername", "customer", "client", "clientname", "buyer", "auftraggeber", "kunde"],
            ["CustomerAddress"] = ["customeraddress", "clientaddress", "buyeraddress", "auftraggeberadresse", "kundenadresse"],
            ["CustomerRepresentative"] = ["customerrepresentative", "clientrepresentative", "customercontact", "vertretenderauftraggeber"]
        };

        private static readonly Dictionary<string, string[]> TermAliases = new()
        {
            ["EffectiveDate"] = ["effectivedate", "commencementdate", "inkrafttreten"],
            ["StartDate"] = ["startdate", "start", "begin", "beginn", "laufzeitbeginn"],
            ["EndDate"] = ["enddate", "end", "expiry", "expirydate", "ende", "laufzeitende"],
            ["Duration"] = ["duration", "term", "contractterm", "laufzeit"],
            ["RenewalRules"] = ["renewal", "renewalrules", "autorenewal", "verlaengerung", "verlängerung"],
            ["TerminationNotice"] = ["termination", "terminationnotice", "noticeperiod", "kuendigungsfrist", "kündigungsfrist"],
            ["PaymentSchedule"] = ["paymentschedule", "payment", "paymentterms", "zahlungsbedingungen", "zahlungsplan"],
            ["PaymentDueDates"] = ["paymentduedates", "duedates", "faelligkeit", "fälligkeit"],
            ["Deposit"] = ["deposit", "advance", "prepayment", "anzahlung"],
            ["BillingCycle"] = ["billingcycle", "billingfrequency", "abrechnungszyklus"],
            ["RecurringCharges"] = ["recurringcharges", "recurring", "wiederkehrendekosten"],
            ["VatTreatment"] = ["vattreatment", "taxtreatment", "vat", "tax", "umsatzsteuer", "mehrwertsteuer"],
            ["Discounts"] = ["discounts", "discount", "rabatt", "nachlass"],
            ["CustomerResponsibilities"] = ["customerresponsibilities", "clientresponsibilities", "mitwirkungspflichten"],
            ["ProviderResponsibilities"] = ["providerresponsibilities", "supplierresponsibilities", "leistungen"],
            ["AcceptanceCriteria"] = ["acceptancecriteria", "acceptance", "abnahme"],
            ["Revisions"] = ["revisions", "revisionrounds", "korrekturschleifen"],
            ["Assumptions"] = ["assumptions", "annahmen"],
            ["Exclusions"] = ["exclusions", "outofscope", "nichtenthalten"],
            ["Warranty"] = ["warranty", "guarantee", "gewaehrleistung", "gewährleistung"],
            ["Liability"] = ["liability", "haftung"],
            ["Confidentiality"] = ["confidentiality", "nda", "vertraulichkeit"],
            ["IntellectualProperty"] = ["intellectualproperty", "ip", "copyright", "nutzungsrechte"],
            ["SignatureParties"] = ["signatureparties", "signatories", "unterzeichner"]
        };

        private static void MapParties(
            Dictionary<string, string?> parties, ContractExtractionDto dto, List<string> unmatched) =>
            MapDictionary(parties, PartyAliases, dto, unmatched);

        private static void MapContractTerms(
            Dictionary<string, string?> terms, ContractExtractionDto dto, List<string> unmatched) =>
            MapDictionary(terms, TermAliases, dto, unmatched);

        private static void MapDictionary(
            Dictionary<string, string?> source,
            Dictionary<string, string[]> aliases,
            ContractExtractionDto dto,
            List<string> unmatched)
        {
            if (source is null) return;

            foreach (var (key, value) in source)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

                var normalised = Normalise(key);

                var target = aliases.FirstOrDefault(a => a.Value.Contains(normalised)).Key;

                var property = target is null
                    ? null
                    : typeof(ContractExtractionDto).GetProperty(target);

                if (property is null)
                {
                    unmatched.Add($"{key.Trim()}: {value.Trim()}");
                    continue;
                }

                property.SetValue(dto, Read(value));
            }
        }

        // ---- positions -----------------------------------------------------

        /// <summary>
        /// A validated commercial term, shown as a candidate position.
        ///
        /// Only what the term actually carries is copied. A term priced per hour
        /// with no agreed number of hours has a rate and no line total, and that is
        /// how it is shown — the old screen's habit of filling a blank total with a
        /// rate is what made variable work look like committed money.
        /// </summary>
        private static ExtractedPositionDto ToPosition(CommercialTerm term)
        {
            var lineTotal = term.FixedAmount is { HasValue: true } fixedAmount
                ? fixedAmount.Value
                : term.Quantity.HasValue && term.UnitRate.HasValue
                    ? term.Quantity.Value * term.UnitRate.Value!.Value
                    : null;

            // A contract position carries a quantity and a line total and cannot say
            // "not stated". A charge with no calculable amount therefore has no
            // honest position to become, and offering one would put a figure on the
            // contract that nobody agreed to.
            var blocked = lineTotal is null
                ? term.UnitRate.HasValue
                    ? $"Priced at a rate with no agreed {(string.IsNullOrWhiteSpace(term.QuantityUnit) ? "quantity" : term.QuantityUnit)}, so it has no line total."
                    : "No amount could be read for this, so it has no line total."
                : null;

            return new ExtractedPositionDto
            {
                // A row with no title cannot be reviewed, so it gets a placeholder
                // title and keeps its figures. Dropping the row instead would hide
                // an amount somebody may owe.
                Title = string.IsNullOrWhiteSpace(term.Name) ? "(untitled position)" : term.Name,
                Description = Describe(term),
                Quantity = term.Quantity,
                Unit = term.QuantityUnit,
                UnitPrice = term.UnitRate.Value,
                LineTotal = lineTotal,
                Currency = term.UnitRate.Currency ?? term.FixedAmount?.Currency,
                VatRatePercent = term.TaxRatePercent,

                BillingCycle = ToBillingCycle(term.BillingRecurrence),
                BillingCyclePhrase = term.BillingRecurrence.SourcePhrase,

                Commitment = term.Commitment.ToString(),
                TermKey = term.Key,

                CanBecomePosition = blocked is null,
                BlockedReason = blocked,

                SourceText = term.Provenance?.SourceSnippet,
                Confidence = term.Provenance?.Confidence ?? 0d,

                // Never pre-ticked. A position becomes real when a person says so.
                Accepted = false
            };
        }

        /// <summary>
        /// The document's billing frequency as one of the application's own cycles,
        /// or null where it has no equivalent.
        ///
        /// The application knows five: one-off, monthly, quarterly, half-yearly,
        /// yearly. A contract may bill weekly, per milestone, or on a condition, and
        /// there is no member for those. Null says so; it does not mean one-off.
        /// </summary>
        private static string? ToBillingCycle(Recurrence recurrence)
        {
            if (recurrence.Kind == RecurrenceKind.None) return "OneTime";
            if (recurrence.Kind != RecurrenceKind.Periodic || recurrence.Unit is null) return null;

            var interval = Math.Max(1, recurrence.Interval);

            return (recurrence.Unit, interval) switch
            {
                (PeriodUnit.Month, 1) => "Monthly",
                (PeriodUnit.Month, 3) => "Quarterly",
                (PeriodUnit.Month, 6) => "SemiAnnual",
                (PeriodUnit.Month, 12) => "Annual",
                (PeriodUnit.Quarter, 1) => "Quarterly",
                (PeriodUnit.Quarter, 2) => "SemiAnnual",
                (PeriodUnit.Quarter, 4) => "Annual",
                (PeriodUnit.Year, 1) => "Annual",

                // Weekly, daily, every two months, every eighteen months: real
                // arrangements the enum cannot hold. Reported as unmapped so a
                // person chooses, rather than being rounded to the nearest member.
                _ => null
            };
        }

        /// <summary>
        /// The term's own description, plus the facts a reader needs to judge the
        /// figure beside it: how firm it is, and anything left open.
        /// </summary>
        private static string? Describe(CommercialTerm term)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(term.Description))
                parts.Add(term.Description!.Trim());

            if (term.Commitment != Commitment.Committed)
                parts.Add($"[{term.Commitment}]");

            if (term.OpenQuestions.Count > 0)
                parts.Add(string.Join(" ", term.OpenQuestions));

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        // ---- money ---------------------------------------------------------

        /// <summary>
        /// The figures come from the deterministic engine, not from the analyser.
        /// Only the committed net is offered as the contract total: estimated,
        /// variable and optional money is real money but nobody has agreed to owe
        /// it, and adding it to a contract value would overstate what is due.
        /// </summary>
        private static void ApplyFinancials(ContractFinancials? financials, ContractExtractionDto dto)
        {
            if (financials is null)
            {
                dto.PriceMissing = dto.Positions.Count == 0;
                return;
            }

            dto.Currency = Read(financials.Currency, 0.9d);

            if (financials.CommittedNet > 0m)
            {
                dto.TotalPrice = Read(
                    financials.CommittedNet.ToString("0.00", CultureInfo.InvariantCulture),
                    financials.IsPartial ? 0.5d : 0.9d);
            }

            // A contract can genuinely have no committed value — everything in it
            // priced per unit, or per hour, with nothing agreed in advance. That is
            // a finding, not an omission, and it is recorded as one so no layer
            // downstream fills the gap.
            dto.PriceMissing = financials.CommittedNet == 0m;
        }

        // ---- what a person has to look at ----------------------------------

        private static void CollectWarnings(
            SemanticAnalysisResult result, SemanticExtractionDto extraction, ContractExtractionDto dto)
        {
            var warnings = new List<string>();

            if (extraction.Warnings is { Count: > 0 })
                warnings.AddRange(extraction.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)));

            if (extraction.OpenQuestions is { Count: > 0 })
                warnings.AddRange(extraction.OpenQuestions.Where(q => !string.IsNullOrWhiteSpace(q)));

            // Validation issues name the term they belong to, so a reader can find
            // the line rather than being told something somewhere is wrong.
            foreach (var issue in result.Issues)
            {
                var name = result.Terms.FirstOrDefault(t => t.Key == issue.TermKey)?.Name;

                warnings.Add(string.IsNullOrWhiteSpace(name)
                    ? $"{issue.Field}: {issue.Message}"
                    : $"{name} — {issue.Field}: {issue.Message}");
            }

            // Amounts the engine refused to total are listed in the financial
            // breakdown, one per line with the term named — a better place for them
            // than a flat warning list, so only the fact that the total is a floor
            // is repeated here.
            if (result.Financials is not null && result.Financials.IsPartial &&
                result.Financials.CommittedNet > 0m)
            {
                warnings.Add(
                    "The committed total covers only the amounts that could be calculated. " +
                    "Some items are not included in it.");
            }

            // A proposal with nothing usable in it, and why. Kept because "the
            // analyser found nothing here" and "the analyser discarded what it
            // found" are different things to a person deciding whether to trust it.
            warnings.AddRange(result.DiscardedReasons.Where(r => !string.IsNullOrWhiteSpace(r)));

            // Charges that cannot become priced lines. Said once, here, so their
            // absence from the positions is a stated fact rather than something the
            // user has to notice for themselves.
            var unpriceable = dto.Positions
                .Where(p => !p.CanBecomePosition)
                .Select(p => $"{p.Title} cannot be added as a position: {p.BlockedReason}")
                .ToList();

            warnings.AddRange(unpriceable);

            // A billing frequency the application's own vocabulary cannot express.
            // Left unsaid, it would be filled in with one-off on the way across.
            foreach (var position in dto.Positions.Where(p =>
                         p.BillingCycle is null && !string.IsNullOrWhiteSpace(p.BillingCyclePhrase)))
            {
                warnings.Add(
                    $"{position.Title} is billed \"{position.BillingCyclePhrase}\", which is not one of the " +
                    "billing cycles here. Choose one when you add it.");
            }

            // A contract with no committed price at all is worth saying out loud.
            // Left as a flag alone it reads as an oversight; said plainly, it is a
            // decision the user is asked to confirm before the contract goes out.
            //
            // Matched on this exact sentence rather than on the word "price": the
            // blocked-position messages say "Priced at a rate…", and a substring
            // guard on "price" let one of those suppress the statement entirely.
            const string noPriceWarning =
                "This contract names no committed price. Nothing has been filled in. Confirm that it is " +
                "deliberately without one before sending it.";

            if (dto.PriceMissing && !warnings.Contains(noPriceWarning))
                warnings.Add(noPriceWarning);

            dto.Warnings = warnings.Distinct().ToList();
        }
    }
}
