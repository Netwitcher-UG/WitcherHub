using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Models.DTO.Services;
using static WitcherHub.Infrastructure.Data.Models.Enums;

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

                RuleFor(x => x.PricingRules)
              .Must(HaveUniquePriorities)
              .WithMessage("Priority must be unique per service (duplicates found).");
            });
        }

        private static bool HaveUniquePriorities(IReadOnlyCollection<PricingRuleDto>? rules)
        {
            if (rules is null || rules.Count == 0) return true;

            var priorities = rules.Select(r => r.Priority).ToList();
            return priorities.Distinct().Count() == priorities.Count;
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

            RuleFor(x => x.Scope)
                .NotEmpty()
                .MaximumLength(30)
                .Must(s => s == "LINE_ITEM" || s == "INVOICE")
                .WithMessage("Scope must be LINE_ITEM or INVOICE.");

            RuleFor(x => x)
                .Must(x => x.ValidFrom is null || x.ValidTo is null || x.ValidFrom <= x.ValidTo)
                .WithMessage("ValidFrom must be <= ValidTo.");

            // ✅ أهم جزء: Expression + Action-based validation
            RuleFor(x => x).Custom((rule, ctx) =>
            {
                // 1) ConditionExpr لازم يكون Expression صالح
                if (!JsExprGuard.TryParseExpression(rule.ConditionExpr, out var condErr))
                    ctx.AddFailure(nameof(rule.ConditionExpr), $"Invalid ConditionExpr: {condErr}");

                // 2) ValueExpr حسب Action
                if (rule.Action == RuleAction.Discount)
                {
                    // Discount عندك بالداتا: 0.10 = 10%
                    if (!JsExprGuard.TryParseDecimalLiteral(rule.ValueExpr, out var d))
                    {
                        ctx.AddFailure(nameof(rule.ValueExpr),
                            "Discount ValueExpr must be a numeric literal like 0.10 (10%).");
                        return;
                    }

                    // منع 0 أو قيم سالبة أو أكبر من 1
                    if (d <= 0 || d > 1)
                    {
                        ctx.AddFailure(nameof(rule.ValueExpr),
                            "Discount must be > 0 and <= 1 (e.g. 0.10 = 10%).");
                    }
                }
                else
                {
                    // باقي الـ actions: ValueExpr غالبًا expression (أو رقم literal) => نتحقق syntax
                    if (!JsExprGuard.TryParseExpression(rule.ValueExpr, out var valErr))
                        ctx.AddFailure(nameof(rule.ValueExpr), $"Invalid ValueExpr: {valErr}");

                    // (اختياري) إذا Multiply وكان literal، امنعي <=0 لأنه خطر
                    if (rule.Action == RuleAction.Multiply &&
                        JsExprGuard.TryParseDecimalLiteral(rule.ValueExpr, out var m) &&
                        m <= 0)
                    {
                        ctx.AddFailure(nameof(rule.ValueExpr), "Multiply factor must be > 0.");
                    }
                }
            });
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
