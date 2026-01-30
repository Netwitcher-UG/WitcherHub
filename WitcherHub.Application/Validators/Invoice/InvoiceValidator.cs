using FluentValidation;
using WitcherHub.Application.Models.DTO.Invoices;

namespace WitcherHub.Application.Validators.Invoice
{
    public sealed class InvoiceValidator : AbstractValidator<InvoiceDTOs>
    {
        public InvoiceValidator()
        {
            RuleFor(x => x.Invoice).NotNull().SetValidator(new InvoiceDtoValidator());
            When(x => x.Items != null && x.Items.Count > 0, () =>
            {
                RuleForEach(x => x.Items).SetValidator(new InvoiceItemDtoValidator());
            });
        }
    }

    public sealed class InvoiceDtoValidator : AbstractValidator<InvoiceDto>
    {
        public InvoiceDtoValidator()
        {
            RuleFor(x => x.ProjectId).NotEmpty();
            RuleFor(x => x.Currency).NotEmpty().Length(3, 10);

            RuleFor(x => x.DueDate)
                .GreaterThan(x => x.IssueDate)
                .When(x => x.IssueDate.HasValue && x.DueDate.HasValue);

            RuleFor(x => x.Status).IsInEnum();

            When(x => x.InvoiceDiscountType.HasValue, () =>
            {
                RuleFor(x => x.InvoiceDiscountValue).NotNull().GreaterThanOrEqualTo(0);
            });
        }
    }

    public sealed class InvoiceItemDtoValidator : AbstractValidator<InvoiceItemDto>
    {
        public InvoiceItemDtoValidator()
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

    public sealed class UpdateInvoiceDtoValidator : AbstractValidator<UpdateInvoiceDto>
    {
        public UpdateInvoiceDtoValidator()
        {
            RuleFor(x => x.Invoice).NotNull().SetValidator(new InvoiceDtoValidator());
            RuleForEach(x => x.Items).SetValidator(new InvoiceItemDtoValidator());

        }
    }

    public sealed class CreateInvoiceItemDtoValidator : AbstractValidator<CreateInvoiceItemDto>
    {
        public CreateInvoiceItemDtoValidator()
        {
            RuleFor(x => x.InvoiceId).NotEmpty();
            RuleFor(x => x.Item).NotNull().SetValidator(new InvoiceItemDtoValidator());
        }
    }

    public sealed class UpdateInvoiceItemDtoValidator : AbstractValidator<UpdateInvoiceItemDto>
    {
        public UpdateInvoiceItemDtoValidator()
        {
            RuleFor(x => x.InvoiceId).NotEmpty();
            RuleFor(x => x.ItemId).NotEmpty();
            RuleFor(x => x.Item).NotNull().SetValidator(new InvoiceItemDtoValidator());
        }
    }

    public sealed class DeleteInvoiceItemDtoValidator : AbstractValidator<DeleteInvoiceItemDto>
    {
        public DeleteInvoiceItemDtoValidator()
        {
            RuleFor(x => x.InvoiceId).NotEmpty();
            RuleFor(x => x.ItemId).NotEmpty();
        }
    }
}
