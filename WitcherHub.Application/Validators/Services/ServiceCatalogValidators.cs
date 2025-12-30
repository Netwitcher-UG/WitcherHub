using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Models.DTO.Services;

namespace WitcherHub.Application.Validators.Services
{
    public sealed class ServiceCatalogDTOsValidator : AbstractValidator<ServiceCatalogDTOs>
    {
        public ServiceCatalogDTOsValidator()
        {
            RuleFor(x => x.Service)
                .NotNull()
                .SetValidator(new ServiceCatalogItemDtoValidator());

            // PricingRules اختيارية — لو بدك تجبرها بالإنشاء غيّر الشرط
            When(x => x.PricingRules is not null && x.PricingRules.Count > 0, () =>
            {
                RuleForEach(x => x.PricingRules).SetValidator(new PricingRuleDtoValidator());
            });
        }
    }

    public sealed class UpdateServiceCatalogItemDtoValidator : AbstractValidator<UpdateServiceCatalogItemDto>
    {
        public UpdateServiceCatalogItemDtoValidator()
        {
            RuleFor(x => x.Service)
                .NotNull()
                .SetValidator(new ServiceCatalogItemDtoValidator());
        }
    }

    public sealed class ServiceCatalogItemDtoValidator : AbstractValidator<ServiceCatalogItemDto>
    {
        public ServiceCatalogItemDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(250);

            RuleFor(x => x.ServiceType).IsInEnum();
            RuleFor(x => x.PricingModel).IsInEnum();

            RuleFor(x => x.BasePrice)
                .GreaterThanOrEqualTo(0).WithMessage("BasePrice must be >= 0.");

            RuleFor(x => x.DefaultCurrency)
                .NotEmpty()
                .MaximumLength(10);

            // JSON schema validation (إذا موجودة وغير فارغة)
            RuleFor(x => x.ConfigSchemaJson)
                .Must(BeValidJson)
                .When(x => !string.IsNullOrWhiteSpace(x.ConfigSchemaJson))
                .WithMessage("ConfigSchemaJson must be valid JSON.");
        }

        private static bool BeValidJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;
            try
            {
                JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class PricingRuleDtoValidator : AbstractValidator<PricingRuleDto>
    {
        public PricingRuleDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Rule name is required.")
                .MaximumLength(200);

            RuleFor(x => x.Priority)
                .InclusiveBetween(0, 10_000);

            RuleFor(x => x.ConditionExpr)
                .NotEmpty()
                .MaximumLength(2000);

            RuleFor(x => x.ValueExpr)
                .NotEmpty()
                .MaximumLength(2000);

            RuleFor(x => x.Label)
                .MaximumLength(200)
                .When(x => !string.IsNullOrWhiteSpace(x.Label));

            RuleFor(x => x.Scope)
                .NotEmpty()
                .MaximumLength(30)
                .Must(s => s == "LINE_ITEM" || s == "INVOICE")
                .WithMessage("Scope must be LINE_ITEM or INVOICE.");

            RuleFor(x => x)
                .Must(x => x.ValidFrom is null || x.ValidTo is null || x.ValidFrom <= x.ValidTo)
                .WithMessage("ValidFrom must be <= ValidTo.");
        }
    }

    public sealed class CreatePricingRuleDtoValidator : AbstractValidator<CreatePricingRuleDto>
    {
        public CreatePricingRuleDtoValidator()
        {
            RuleFor(x => x.ServiceId).NotEmpty();
            RuleFor(x => x.Rule).NotNull().SetValidator(new PricingRuleDtoValidator());
        }
    }

    public sealed class UpdatePricingRuleDtoValidator : AbstractValidator<UpdatePricingRuleDto>
    {
        public UpdatePricingRuleDtoValidator()
        {
            RuleFor(x => x.ServiceId).NotEmpty();
            RuleFor(x => x.RuleId).NotEmpty();
            RuleFor(x => x.Rule).NotNull().SetValidator(new PricingRuleDtoValidator());
        }
    }

    public sealed class DeletePricingRuleDtoValidator : AbstractValidator<DeletePricingRuleDto>
    {
        public DeletePricingRuleDtoValidator()
        {
            RuleFor(x => x.ServiceId).NotEmpty();
            RuleFor(x => x.RuleId).NotEmpty();
        }
    }
}
