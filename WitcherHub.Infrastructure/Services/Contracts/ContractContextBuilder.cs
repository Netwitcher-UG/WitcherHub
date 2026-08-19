using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Assembles everything the generator is allowed to know, from the record.
    ///
    /// The generator used to be handed the positions and the currency, which is
    /// why the wording it produced could not name the parties, the project or the
    /// term — and why a contract built from a supplied document had that document
    /// pasted in instead of being written from it.
    ///
    /// Each source is read from where it is actually kept: the provider from
    /// company settings, the customer from the customer record reached through
    /// the project, the terms from what a person confirmed. Nothing is read out
    /// of the pasted text, which arrives as context and carries the least weight
    /// of anything here.
    /// </summary>
    public sealed class ContractContextBuilder
    {
        private readonly AppDbContext _db;
        private readonly ContractTemplateOptions _template;

        public ContractContextBuilder(AppDbContext db, ContractTemplateOptions template)
        {
            _db = db;
            _template = template;
        }

        public async Task<ContractGenerationContext> BuildAsync(
            Contract contract,
            IReadOnlyList<ManualPositionDto> positions,
            PositionTotalsDto? totals,
            string? sourceText,
            string? additionalInstructions,
            string language,
            CancellationToken ct = default)
        {
            var customer = await LoadCustomerAsync(contract, ct);
            var project = await LoadProjectAsync(contract, ct);

            return new ContractGenerationContext
            {
                Provider = ProviderFromSettings(),
                Customer = customer,
                Project = project,

                Contract = new ContractGenerationContext.ContractDetailsContext(
                    ContractNo: contract.ContractNo,
                    Currency: contract.Currency ?? "EUR",
                    StartDate: contract.StartDate,
                    EndDate: contract.EndDate,
                    AgreedTotalNet: contract.AgreedTotalNet,
                    VatRatePercent: contract.AgreedTotalVatRatePercent,
                    PaymentTerms: NullIfBlank(contract.PaymentTermsText),
                    Introduction: null),

                Positions = positions,
                Totals = totals,
                ConfirmedTerms = await LoadConfirmedTermsAsync(contract, ct),

                // Optional, and the last word on nothing.
                SourceText = NullIfBlank(sourceText),
                AdditionalInstructions = NullIfBlank(additionalInstructions),
                Language = string.IsNullOrWhiteSpace(language) ? "de" : language
            };
        }

        /// <summary>
        /// Us, from company settings.
        ///
        /// Deliberately never from a supplied document: a customer's old contract
        /// names whichever agency wrote it, and letting that through would put
        /// somebody else's company at the head of our contract.
        /// </summary>
        private ContractGenerationContext.PartyContext ProviderFromSettings()
        {
            var lines = (_template.ProviderBlock ?? "")
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            return new ContractGenerationContext.PartyContext(
                Name: lines.FirstOrDefault(),
                Address: lines.Count > 1 ? string.Join("\n", lines.Skip(1)) : null);
        }

        private async Task<ContractGenerationContext.PartyContext> LoadCustomerAsync(
            Contract contract, CancellationToken ct)
        {
            var customer = await _db.Contracts
                .AsNoTracking()
                .Where(c => c.Id == contract.Id)
                .Select(c => new
                {
                    c.Project.Customer.Name,
                    c.Project.Customer.TaxId,

                    Address = c.Project.Customer.Addresses
                        .OrderByDescending(a => a.IsDefault)
                        .Select(a => new { a.StreetRaw, a.AddressLine2, a.PostalCode, a.City, a.Country })
                        .FirstOrDefault(),

                    Email = c.Project.Customer.EmailAddresses
                        .OrderByDescending(e => e.Kind == "business")
                        .Select(e => e.Email)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct);

            if (customer is null)
                return new ContractGenerationContext.PartyContext(null, null);

            var address = customer.Address is null
                ? null
                : string.Join("\n", new[]
                {
                    customer.Address.StreetRaw,
                    customer.Address.AddressLine2,
                    $"{customer.Address.PostalCode} {customer.Address.City}".Trim(),
                    customer.Address.Country
                }.Where(l => !string.IsNullOrWhiteSpace(l)));

            return new ContractGenerationContext.PartyContext(
                Name: customer.Name,
                Address: address,
                Email: customer.Email,
                TaxId: customer.TaxId);
        }

        private async Task<ContractGenerationContext.ProjectContext> LoadProjectAsync(
            Contract contract, CancellationToken ct)
        {
            var project = await _db.Set<Project>()
                .AsNoTracking()
                .Where(p => p.Id == contract.ProjectId)
                .Select(p => new { p.Title, p.Description, p.StartDate, p.EndDate })
                .FirstOrDefaultAsync(ct);

            return project is null
                ? new ContractGenerationContext.ProjectContext(null)
                : new ContractGenerationContext.ProjectContext(
                    project.Title, NullIfBlank(project.Description), project.StartDate, project.EndDate);
        }

        /// <summary>
        /// The commercial facts a person ticked while reviewing a supplied
        /// document. These are not source text — they were read out of it and
        /// then agreed to, which is what makes them usable.
        /// </summary>
        private async Task<IReadOnlyList<ContractGenerationContext.ConfirmedTerm>> LoadConfirmedTermsAsync(
            Contract contract, CancellationToken ct)
        {
            var stored = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contract.Id && d.ExtractedTerms != null)
                .OrderByDescending(d => d.Version)
                .Select(d => d.ExtractedTerms)
                .FirstOrDefaultAsync(ct);

            if (stored is null) return Array.Empty<ContractGenerationContext.ConfirmedTerm>();

            ContractExtractionDto? extraction;

            try
            {
                extraction = System.Text.Json.JsonSerializer
                    .Deserialize<ContractExtractionDto>(stored.RootElement.GetRawText());
            }
            catch (System.Text.Json.JsonException)
            {
                // An extraction in an older shape is not worth failing generation
                // over; the authoritative data above stands on its own.
                return Array.Empty<ContractGenerationContext.ConfirmedTerm>();
            }

            if (extraction is null) return Array.Empty<ContractGenerationContext.ConfirmedTerm>();

            var terms = new List<ContractGenerationContext.ConfirmedTerm>();

            void Add(string label, ExtractedValue value)
            {
                if (value.Confirmed && value.HasValue)
                    terms.Add(new ContractGenerationContext.ConfirmedTerm(label, value.Value!.Trim()));
            }

            Add("Vertragsart", extraction.ContractType);
            Add("Laufzeit", extraction.Duration);
            Add("Verlängerung", extraction.RenewalRules);
            Add("Kündigungsfrist", extraction.TerminationNotice);
            Add("Gesamtpreis", extraction.TotalPrice);
            Add("Währung", extraction.Currency);
            Add("Umsatzsteuer", extraction.VatRate);
            Add("Steuerliche Behandlung", extraction.VatTreatment);
            Add("Rabatte", extraction.Discounts);
            Add("Abrechnungszyklus", extraction.BillingCycle);
            Add("Zahlungsplan", extraction.PaymentSchedule);
            Add("Fälligkeit", extraction.PaymentDueDates);
            Add("Anzahlung", extraction.Deposit);
            Add("Mitwirkungspflichten", extraction.CustomerResponsibilities);
            Add("Abnahme", extraction.AcceptanceCriteria);
            Add("Wiederkehrende Entgelte", extraction.RecurringCharges);

            return terms;
        }

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
