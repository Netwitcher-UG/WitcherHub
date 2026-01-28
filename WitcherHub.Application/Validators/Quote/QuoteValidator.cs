using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Application.Models.DTO.Quotes;

namespace WitcherHub.Application.Validators.Quote
{
    public sealed class QuoteValidator : AbstractValidator<QuoteDTOs>
    {
        public QuoteValidator()
        {
            RuleFor(x => x.Quote).NotNull().SetValidator(new QuoteDtoValidator());
            RuleFor(x => x.Items).NotNull();

            RuleForEach(x => x.Items).SetValidator(new QuoteItemDtoValidator());
        }
    }

    public sealed class QuoteDtoValidator : AbstractValidator<QuoteDto>
    {
        public QuoteDtoValidator()
        {
            RuleFor(x => x.ProjectId).NotEmpty();
            RuleFor(x => x.Currency).NotEmpty().Length(3, 10);

            RuleFor(x => x.ExpiresAt)
                .GreaterThan(x => x.IssuedAt)
                .When(x => x.IssuedAt.HasValue && x.ExpiresAt.HasValue);

            RuleFor(x => x.Status).IsInEnum();
        }
    }

    public sealed class QuoteItemDtoValidator : AbstractValidator<QuoteItemDto>
    {
        public QuoteItemDtoValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
            RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
            RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1_000_000);

            When(x => x.DiscountType.HasValue, () =>
            {
                RuleFor(x => x.DiscountValue).NotNull().GreaterThanOrEqualTo(0);
            });

            RuleFor(x => x.Position).GreaterThan(0);
        }
    }

    public sealed class UpdateQuoteDtoValidator : AbstractValidator<UpdateQuoteDto>
    {
        public UpdateQuoteDtoValidator()
        {
            RuleFor(x => x.Quote).NotNull().SetValidator(new QuoteDtoValidator());
            RuleForEach(x => x.Items).SetValidator(new QuoteItemDtoValidator());
        }
    }

    public sealed class CreateQuoteItemDtoValidator : AbstractValidator<CreateQuoteItemDto>
    {
        public CreateQuoteItemDtoValidator()
        {
            RuleFor(x => x.QuoteId).NotEmpty();
            RuleFor(x => x.Item).NotNull().SetValidator(new QuoteItemDtoValidator());
        }
    }

    public sealed class UpdateQuoteItemDtoValidator : AbstractValidator<UpdateQuoteItemDto>
    {
        public UpdateQuoteItemDtoValidator()
        {
            RuleFor(x => x.QuoteId).NotEmpty();
            RuleFor(x => x.ItemId).NotEmpty();
            RuleFor(x => x.Item).NotNull().SetValidator(new QuoteItemDtoValidator());
        }
    }

    public sealed class DeleteQuoteItemDtoValidator : AbstractValidator<DeleteQuoteItemDto>
    {
        public DeleteQuoteItemDtoValidator()
        {
            RuleFor(x => x.QuoteId).NotEmpty();
            RuleFor(x => x.ItemId).NotEmpty();
        }
    }
}
