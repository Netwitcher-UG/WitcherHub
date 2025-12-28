using FluentValidation;
using WitcherHub.Application.Models.DTO.Customers;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Validators.Customers
{
    public class CreateCustomerDtoValidator : AbstractValidator<CreateCustomerDto>
    {
        public CreateCustomerDtoValidator()
        {
            RuleFor(x => x.Customer.Type)
                .IsInEnum();

            RuleFor(x => x.Customer.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(200);

            RuleFor(x => x.Customer.Email)
                .EmailAddress().WithMessage("Invalid email.")
                .When(x => !string.IsNullOrWhiteSpace(x.Customer.Email));

            RuleFor(x => x.Contact.Email)
                .EmailAddress().WithMessage("Invalid contact email.")
                .When(x => !string.IsNullOrWhiteSpace(x.Contact.Email));

            When(x => x.Customer.Type == CustomerType.Company, () =>
            {
                RuleFor(x => x.Contact.Name)
                    .NotEmpty().WithMessage("Contact name is required for company.");
            });
        }
    }
}
