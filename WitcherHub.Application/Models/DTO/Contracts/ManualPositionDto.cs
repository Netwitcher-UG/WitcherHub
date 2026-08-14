using System.Text.Json.Serialization;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Contracts
{
    /// <summary>
    /// A contract position entered by hand, with no Service Catalog record behind
    /// it. <see cref="CatalogServiceId"/> stays null for a manual position — the
    /// reference is nullable rather than pointing at a placeholder catalog entry.
    ///
    /// This is also the shape the AI organizer must return, so the two paths agree
    /// on one vocabulary.
    /// </summary>
    public sealed class ManualPositionDto
    {
        // ---- identity -------------------------------------------------------

        /// <summary>Client-side identity, so a list can be reordered and diffed before it is saved.</summary>
        public string ClientId { get; set; } = Guid.NewGuid().ToString("n");

        public Guid? ContractItemId { get; set; }

        public ContractItemSource SourceType { get; set; } = ContractItemSource.Manual;

        /// <summary>Null for a manual position. Set only when the line came from the catalog.</summary>
        public Guid? CatalogServiceId { get; set; }

        /// <summary>
        /// The supplied contract version this position was read out of, for a
        /// position with source <see cref="ContractItemSource.ExtractedFromContractText"/>.
        /// Keeps the position attached to the document that justifies it.
        /// </summary>
        public Guid? SourceDraftId { get; set; }

        public int Position { get; set; } = 1;

        // ---- what is being sold --------------------------------------------

        public string Title { get; set; } = "";
        public string? ServiceType { get; set; }
        public string? Description { get; set; }
        public string? Scope { get; set; }
        public List<string> Deliverables { get; set; } = new();

        // ---- commercial terms ----------------------------------------------
        // Everything below is owned by the user. The AI organizer may reword the
        // descriptive fields above but must never alter these.

        public decimal Quantity { get; set; } = 1;
        public string? Unit { get; set; }
        public PricingModel PricingModel { get; set; } = PricingModel.Fixed;
        public decimal? UnitPrice { get; set; }
        public string Currency { get; set; } = "EUR";
        public decimal? VatRate { get; set; }
        public DiscountType? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }
        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;
        public int? DurationPeriods { get; set; }

        /// <summary>
        /// Deliberately supplied at no charge. The only case where a price is not
        /// required.
        /// </summary>
        public bool IsFree { get; set; }

        // ---- delivery -------------------------------------------------------

        public string? DeliveryMethod { get; set; }
        public ActivationMethod ActivationMethod { get; set; } = ActivationMethod.NotApplicable;
        public DateOnly? StartDate { get; set; }
        public DateOnly? DeliveryDate { get; set; }

        // ---- expectations ---------------------------------------------------

        public List<string> AcceptanceCriteria { get; set; } = new();
        public List<string> CustomerResponsibilities { get; set; } = new();
        public List<string> Assumptions { get; set; } = new();
        public List<string> Exclusions { get; set; } = new();
        public string? Notes { get; set; }

        // ---- derived --------------------------------------------------------

        /// <summary>
        /// Net line total before tax, after any discount. Recomputed server-side;
        /// a value posted by the browser is never trusted.
        /// </summary>
        [JsonIgnore]
        public decimal NetTotal => CalculateNetTotal();

        [JsonIgnore]
        public decimal VatAmount => Math.Round(NetTotal * ((VatRate ?? 0m) / 100m), 2, MidpointRounding.AwayFromZero);

        [JsonIgnore]
        public decimal GrossTotal => NetTotal + VatAmount;

        private decimal CalculateNetTotal()
        {
            if (IsFree)
                return 0m;

            var unit = UnitPrice ?? 0m;
            var quantity = Quantity <= 0 ? 0m : Quantity;

            // A fixed price is the price of the line, not a rate to multiply.
            var gross = PricingModel == PricingModel.Fixed ? unit : unit * quantity;

            var discount = DiscountType switch
            {
                Infrastructure.Data.Models.Enums.DiscountType.Percent =>
                    gross * ((DiscountValue ?? 0m) / 100m),
                Infrastructure.Data.Models.Enums.DiscountType.Amount or
                Infrastructure.Data.Models.Enums.DiscountType.Fixed => DiscountValue ?? 0m,
                _ => 0m
            };

            discount = Math.Clamp(discount, 0m, gross);

            return Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// Totals across a set of positions, recalculated on the server.
    /// </summary>
    public sealed record PositionTotalsDto(
        int PositionCount,
        decimal Subtotal,
        decimal Discount,
        decimal Vat,
        decimal Total,
        string Currency)
    {
        public static PositionTotalsDto From(IReadOnlyCollection<ManualPositionDto> positions, string fallbackCurrency = "EUR")
        {
            if (positions.Count == 0)
                return new PositionTotalsDto(0, 0m, 0m, 0m, 0m, fallbackCurrency);

            var discount = positions.Sum(GrossOf) - positions.Sum(p => p.NetTotal);

            return new PositionTotalsDto(
                PositionCount: positions.Count,
                Subtotal: Math.Round(positions.Sum(p => p.NetTotal), 2, MidpointRounding.AwayFromZero),
                Discount: Math.Round(discount, 2, MidpointRounding.AwayFromZero),
                Vat: Math.Round(positions.Sum(p => p.VatAmount), 2, MidpointRounding.AwayFromZero),
                Total: Math.Round(positions.Sum(p => p.GrossTotal), 2, MidpointRounding.AwayFromZero),
                Currency: positions.First().Currency is { Length: > 0 } c ? c : fallbackCurrency);
        }

        private static decimal GrossOf(ManualPositionDto p)
        {
            if (p.IsFree) return 0m;
            var unit = p.UnitPrice ?? 0m;
            return p.PricingModel == PricingModel.Fixed ? unit : unit * (p.Quantity <= 0 ? 0m : p.Quantity);
        }
    }
}
