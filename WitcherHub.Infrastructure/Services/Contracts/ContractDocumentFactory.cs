using System.Globalization;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// What the contract generator is asked for, from whichever source is
    /// starting a contract.
    ///
    /// A contract can begin in two places — a customer signing a quote, or
    /// somebody building one by hand — and both end at the same generator, the
    /// one that asks the model for the structured Anlage A and merges it into the
    /// Agenturvertrag template. What differs is only where the positions, the
    /// parties and the dates are read from.
    ///
    /// That difference used to be the whole of it: the quote's mapping lived
    /// privately on the signing page and the builder had no mapping at all, so
    /// the two paths produced different documents. Writing a second mapping for
    /// the builder would have produced a third. There is one here, with an entry
    /// point per source, so a change to how a contract is described reaches both
    /// without anyone having to remember the other.
    ///
    /// Everything on it is static and pure: it reads its inputs and returns a
    /// request. Generating, persisting and versioning belong to the callers,
    /// because those genuinely differ — a signed quote creates a contract that
    /// does not exist yet, and the builder adds a version to one that does.
    /// </summary>
    public static class ContractDocumentFactory
    {
        /// <summary>
        /// The request for a contract that follows from a signed quote.
        ///
        /// Moved here verbatim from the signing page, which is why the quote path
        /// is unchanged by this: it is the same mapping, in a place the builder
        /// can also reach.
        /// </summary>
        public static GenerateContractDocumentRequest FromQuote(
            Quote quote,
            string signerName,
            string? signerEmail)
        {
            ArgumentNullException.ThrowIfNull(quote);

            return new GenerateContractDocumentRequest
            {
                ProjectId = quote.ProjectId,
                ContractNo = null,
                ProjectTitle = string.IsNullOrWhiteSpace(quote.Project?.Title)
                    ? "Project"
                    : quote.Project!.Title!,
                Currency = string.IsNullOrWhiteSpace(quote.Currency) ? "EUR" : quote.Currency!,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = null,
                SignerName = signerName,
                SignerEmail = signerEmail,
                LeaveCustomerFieldsBlank = false,
                IncludePricesInServicesSection = true,

                Services = (quote.Items ?? new List<QuoteItem>())
                    .OrderBy(x => x.Position)
                    .Select((x, index) => new ContractServiceLineDto
                    {
                        Position = x.Position > 0 ? x.Position : index + 1,
                        ServiceId = x.ServiceId,
                        Title = string.IsNullOrWhiteSpace(x.Title) ? $"Position {index + 1}" : x.Title.Trim(),

                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        BillingCycle = x.BillingCycle,
                        DiscountType = x.DiscountType,
                        DiscountValue = x.DiscountValue,

                        ServiceName = x.Service?.Name,
                        ServiceType = x.Service?.ServiceType.ToString(),
                        PricingModel = x.Service?.PricingModel.ToString(),
                        AgreedPrice = ResolveQuoteItemAgreedPrice(x),
                        Config = JsonDocumentToDictionary(x.Config)
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// The request for a contract being built by hand, from its own positions.
        ///
        /// The equivalents of the quote's fields, taken from the contract itself:
        /// its project, its currency, its dates, its positions. Two of the quote's
        /// inputs have no equivalent and are not invented:
        ///
        ///   * the signer. Nobody has signed anything — this is the wording being
        ///     written — so the customer stands as the party the contract names,
        ///     and no e-mail is claimed for a signature that has not happened.
        ///   * the supplied document, which a quote never has. It is passed as
        ///     context when there is one, and simply absent when there is not.
        /// </summary>
        public static GenerateContractDocumentRequest FromContract(
            Contract contract,
            IReadOnlyList<ManualPositionDto> positions,
            PartyDetails parties,
            string? additionalInstructions,
            string? suppliedDocument)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(positions);
            ArgumentNullException.ThrowIfNull(parties);

            return new GenerateContractDocumentRequest
            {
                ProjectId = contract.ProjectId,
                ContractNo = contract.ContractNo,
                ProjectTitle = string.IsNullOrWhiteSpace(contract.Project?.Title)
                    ? "Project"
                    : contract.Project!.Title!,
                Currency = string.IsNullOrWhiteSpace(contract.Currency) ? "EUR" : contract.Currency!,
                StartDate = contract.StartDate,
                EndDate = contract.EndDate,

                // No signature has been given, so this names the party rather
                // than claiming somebody signed.
                SignerName = parties.CustomerName ?? "",
                SignerEmail = null,

                LeaveCustomerFieldsBlank = false,

                // The quote path leaves this unset, and the generator then fills
                // the Kunde block with the literal placeholder "Name/Firma:
                // (filled)" — a contract that does not name who it is between.
                // The parties are known here, so they are given.
                CustomerBlockOverride = CustomerBlock(parties),

                IncludePricesInServicesSection = true,

                AdditionalInstructions = WithSuppliedDocument(additionalInstructions, suppliedDocument),

                Services = positions
                    .OrderBy(p => p.Position)
                    .Select((p, index) => new ContractServiceLineDto
                    {
                        Position = p.Position > 0 ? p.Position : index + 1,
                        ServiceId = p.CatalogServiceId,
                        Title = string.IsNullOrWhiteSpace(p.Title) ? $"Position {index + 1}" : p.Title.Trim(),
                        ServiceType = p.ServiceType,
                        Quantity = p.Quantity <= 0 ? 1m : p.Quantity,
                        UnitPrice = p.UnitPrice ?? 0m,
                        BillingCycle = p.BillingCycle,
                        DiscountType = p.DiscountType,
                        DiscountValue = p.DiscountValue,

                        // The net the position comes to — discounts applied, tax
                        // not — which is what the quote passes as well.
                        AgreedPrice = p.NetTotal
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// The customer as the template's Kunde block, in the shape the
        /// generator's own placeholder uses.
        /// </summary>
        private static string CustomerBlock(PartyDetails parties)
        {
            var lines = new List<string> { $"Name/Firma: {parties.CustomerName}".TrimEnd() };

            if (!string.IsNullOrWhiteSpace(parties.CustomerAddress))
                lines.Add($"Adresse: {parties.CustomerAddress!.Replace("\n", ", ").Trim()}");

            return string.Join("\n", lines) + "\n";
        }

        /// <summary>
        /// The user's own instructions, followed by the supplied document if there
        /// is one, labelled for what it is.
        ///
        /// The labelling is not decoration. A pasted agreement names another
        /// agency, other prices and another governing law, and the one thing that
        /// must never happen is it being copied into the contract — the defect
        /// that once showed the customer's old agreement as the contract body. So
        /// it is given as the least authoritative source, and said to be context
        /// rather than content.
        ///
        /// This is also the only channel it has: the generator builds Anlage A
        /// from the positions and takes no document of its own. Without it the
        /// action called "Generate from text and positions" would use only the
        /// positions, and the text would be stored, listed as a version, named in
        /// the button and never read.
        /// </summary>
        private static string? WithSuppliedDocument(string? instructions, string? suppliedDocument)
        {
            if (string.IsNullOrWhiteSpace(suppliedDocument))
                return instructions;

            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(instructions))
                builder.AppendLine(instructions!.Trim()).AppendLine();

            builder.AppendLine(
                "The following document was supplied by the customer. It is context of LOWEST AUTHORITY: " +
                "the positions and the contract record above outrank it wherever they disagree. " +
                "Use it to understand what was agreed. Do not copy it, do not quote it, and do not " +
                "carry over its parties, prices, dates or governing law.");

            builder.AppendLine();
            builder.AppendLine("--- supplied document ---");
            builder.AppendLine(suppliedDocument!.Trim());
            builder.AppendLine("--- end of supplied document ---");

            return builder.ToString();
        }

        /// <summary>
        /// The agreed net for a quote line: what was actually charged, with the
        /// discount taken off and the tax left out, because the contract records
        /// the net that was agreed.
        /// </summary>
        private static decimal? ResolveQuoteItemAgreedPrice(QuoteItem item)
        {
            var baseTotal = item.Quantity * item.UnitPrice;

            var subTotal = ReadDec(item.PriceBreakdown, "subTotal", 0m);
            if (subTotal > 0m)
                return subTotal;

            var baseTotalFromBreakdown = ReadDec(item.PriceBreakdown, "baseTotal", baseTotal);
            var discountAmount = ReadNestedDec(item.PriceBreakdown, "discount", "amount", 0m);
            var netTotal = Math.Max(0m, baseTotalFromBreakdown - discountAmount);

            return netTotal > 0m ? netTotal : baseTotal;
        }

        private static Dictionary<string, object> JsonDocumentToDictionary(JsonDocument? doc)
        {
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in doc.RootElement.EnumerateObject())
                result[property.Name] = JsonElementToObject(property.Value) ?? string.Empty;

            return result;
        }

        private static object? JsonElementToObject(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(
                        x => x.Name,
                        x => JsonElementToObject(x.Value) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase),

                JsonValueKind.Array => element.EnumerateArray()
                    .Select(JsonElementToObject)
                    .ToList(),

                JsonValueKind.String => element.GetString(),

                JsonValueKind.Number => element.TryGetDecimal(out var m)
                    ? m
                    : element.TryGetDouble(out var d)
                        ? d
                        : element.GetRawText(),

                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };

        private static decimal ReadDec(JsonDocument? doc, string prop, decimal fallback = 0m)
        {
            try
            {
                if (doc is null) return fallback;

                if (!doc.RootElement.TryGetProperty(prop, out var value))
                    return fallback;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
                    return decimalValue;

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

        private static decimal ReadNestedDec(JsonDocument? doc, string parent, string prop, decimal fallback = 0m)
        {
            try
            {
                if (doc is null) return fallback;

                if (!doc.RootElement.TryGetProperty(parent, out var parentElement) ||
                    parentElement.ValueKind != JsonValueKind.Object)
                    return fallback;

                if (!parentElement.TryGetProperty(prop, out var value))
                    return fallback;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
                    return decimalValue;

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
    }
}
