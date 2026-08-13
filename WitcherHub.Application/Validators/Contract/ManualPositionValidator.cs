using FluentValidation;
using WitcherHub.Application.Models.DTO.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Validators.Contract
{
    /// <summary>
    /// Rules for a hand-entered contract position.
    ///
    /// A manual position carries no catalog record, so nothing else will supply a
    /// sensible price or unit later — the commercial fields have to be right here.
    /// </summary>
    public sealed class ManualPositionValidator : AbstractValidator<ManualPositionDto>
    {
        public ManualPositionValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Give the position a title.")
                .MaximumLength(250);

            RuleFor(x => x.Position)
                .GreaterThan(0);

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.")
                .LessThanOrEqualTo(1_000_000);

            // Price and currency are required unless the line is deliberately free.
            When(x => !x.IsFree, () =>
            {
                RuleFor(x => x.UnitPrice)
                    .NotNull().WithMessage("Enter a price, or mark the position as free.")
                    .GreaterThanOrEqualTo(0).WithMessage("A price cannot be negative.");

                RuleFor(x => x.Currency)
                    .NotEmpty().WithMessage("Choose a currency.")
                    .Length(3, 10);
            });

            When(x => x.IsFree, () =>
            {
                RuleFor(x => x.UnitPrice)
                    .Must(p => p is null or 0m)
                    .WithMessage("A free position cannot carry a price.");
            });

            RuleFor(x => x.VatRate)
                .InclusiveBetween(0m, 100m).WithMessage("VAT must be between 0 and 100 percent.")
                .When(x => x.VatRate.HasValue);

            RuleFor(x => x.DiscountValue)
                .GreaterThanOrEqualTo(0).WithMessage("A discount cannot be negative.")
                .When(x => x.DiscountValue.HasValue);

            RuleFor(x => x.DiscountValue)
                .InclusiveBetween(0m, 100m)
                .WithMessage("A percentage discount must be between 0 and 100.")
                .When(x => x.DiscountType == DiscountType.Percent && x.DiscountValue.HasValue);

            RuleFor(x => x.DiscountType)
                .NotNull()
                .WithMessage("Choose how the discount should be applied.")
                .When(x => x.DiscountValue is > 0m);

            // A recurring cycle without a period count leaves the contract's term
            // undefined, which is exactly the ambiguity the handover flagged.
            RuleFor(x => x.DurationPeriods)
                .NotNull()
                .WithMessage("State how many billing periods were agreed.")
                .When(x => x.BillingCycle != BillingCycle.OneTime);

            RuleFor(x => x.DurationPeriods)
                .GreaterThan(0).WithMessage("The number of billing periods must be greater than zero.")
                .When(x => x.DurationPeriods.HasValue);

            RuleFor(x => x.StartDate)
                .NotNull()
                .WithMessage("A start date is required when activation happens on a specified date.")
                .When(x => x.ActivationMethod == ActivationMethod.OnSpecifiedDate);

            RuleFor(x => x.DeliveryDate)
                .GreaterThanOrEqualTo(x => x.StartDate!.Value)
                .WithMessage("The delivery date cannot be before the start date.")
                .When(x => x.StartDate.HasValue && x.DeliveryDate.HasValue);

            RuleFor(x => x.CatalogServiceId)
                .Null()
                .WithMessage("A manual position must not reference a catalog service.")
                .When(x => x.SourceType == ContractItemSource.Manual);

            RuleFor(x => x.CatalogServiceId)
                .NotNull()
                .WithMessage("A catalog position must reference a service.")
                .When(x => x.SourceType == ContractItemSource.Catalog);
        }
    }
}
