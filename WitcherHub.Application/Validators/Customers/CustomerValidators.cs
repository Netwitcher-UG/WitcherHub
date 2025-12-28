using FluentValidation;
using WitcherHub.Application.Models.DTO.Customers;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Validators.Customers
{
    public sealed class CustomerDTOsValidator : AbstractValidator<CustomerDTOs>
    {
        public CustomerDTOsValidator()
        {
            RuleFor(x => x.Customer)
                .NotNull()
                .SetValidator(new CustomerDtoValidator());

            RuleFor(x => x.Address)
                .NotNull()
                .SetValidator(new AddressDtoValidator());

            // Contact is always present (not nullable), so validate it only when it has any input OR when Company.
            When(x => x.Customer.Type == CustomerType.Company, () =>
            {
                RuleFor(x => x.Contact)
                    .NotNull()
                    .SetValidator(new ContactDtoValidator());

                // Company contact rules (required)
                RuleFor(x => x.Contact.Name)
                    .NotEmpty().WithMessage("Contact name is required for company.")
                    .MaximumLength(200);

                RuleFor(x => x.Contact)
                    .Must(c => !string.IsNullOrWhiteSpace(c.Email) || !string.IsNullOrWhiteSpace(c.Phone))
                    .WithMessage("Company contact must include at least email or phone.");
            });

            When(x => x.Customer.Type != CustomerType.Company, () =>
            {
                When(x => !IsEmptyContact(x.Contact), () =>
                {
                    RuleFor(x => x.Contact)
                        .SetValidator(new ContactDtoValidator());
                });
            });
        }

        private static bool IsEmptyContact(ContactDto c)
        {
            if (c is null) return true;
            return string.IsNullOrWhiteSpace(c.Name)
                && string.IsNullOrWhiteSpace(c.Email)
                && string.IsNullOrWhiteSpace(c.Phone)
                && string.IsNullOrWhiteSpace(c.Position);
        }
    }

    public sealed class UpdateCustomerDtoValidator : AbstractValidator<UpdateCustomerDto>
    {
        public UpdateCustomerDtoValidator()
        {
            RuleFor(x => x.Customer)
                .NotNull()
                .SetValidator(new CustomerDTOsValidator());
        }
    }

    // ============================
    // Base validators (DI-friendly)
    // ============================

    public sealed class CustomerDtoValidator : AbstractValidator<CustomerDto>
    {
        public CustomerDtoValidator()
        {
            RuleFor(x => x.Type).IsInEnum();

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(250);

            RuleFor(x => x.Email)
                .MaximumLength(320)
                .EmailAddress().WithMessage("Invalid email.")
                .When(x => !string.IsNullOrWhiteSpace(x.Email));

            RuleFor(x => x.Phone)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Phone));

            RuleFor(x => x.TaxId)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.TaxId));
        }
    }

    public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
    {
        public AddressDtoValidator()
        {
            RuleFor(x => x.Label)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Label));

            RuleFor(x => x.Country)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.Country));

            RuleFor(x => x.City)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.City));

            RuleFor(x => x.PostalCode)
                .MaximumLength(30)
                .When(x => !string.IsNullOrWhiteSpace(x.PostalCode));

            RuleFor(x => x.FullNameOrCompany)
                .NotEmpty().WithMessage("Full name / company is required.")
                .MaximumLength(250);

            RuleFor(x => x.Street)
                .NotEmpty().WithMessage("Street is required.")
                .MaximumLength(250);

            RuleFor(x => x.StreetNr)
                .NotEmpty().WithMessage("Street number is required.")
                .MaximumLength(50);
        }
    }

    // ✅ no bool constructor anymore => DI can build it
    public sealed class ContactDtoValidator : AbstractValidator<ContactDto>
    {
        public ContactDtoValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(200)
                .When(x => !string.IsNullOrWhiteSpace(x.Name));

            RuleFor(x => x.Position)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.Position));

            RuleFor(x => x.Email)
                .MaximumLength(320)
                .EmailAddress().WithMessage("Invalid contact email.")
                .When(x => !string.IsNullOrWhiteSpace(x.Email));

            RuleFor(x => x.Phone)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        }
    }

    // ============================
    // Address operations validators
    // ============================

    public sealed class CreateCustomerAddressDtoValidator : AbstractValidator<CreateCustomerAddressDto>
    {
        public CreateCustomerAddressDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Address).NotNull().SetValidator(new AddressDtoValidator());
        }
    }

    public sealed class UpdateCustomerAddressDtoValidator : AbstractValidator<UpdateCustomerAddressDto>
    {
        public UpdateCustomerAddressDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.AddressId).NotEmpty();
            RuleFor(x => x.Address).NotNull().SetValidator(new AddressDtoValidator());
        }
    }

    public sealed class DeleteCustomerAddressDtoValidator : AbstractValidator<DeleteCustomerAddressDto>
    {
        public DeleteCustomerAddressDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.AddressId).NotEmpty();
        }
    }

    public sealed class SetDefaultCustomerAddressDtoValidator : AbstractValidator<SetDefaultCustomerAddressDto>
    {
        public SetDefaultCustomerAddressDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.AddressId).NotEmpty();
        }
    }

    // ============================
    // Contact operations validators
    // ============================

    public sealed class CreateCustomerContactDtoValidator : AbstractValidator<CreateCustomerContactDto>
    {
        public CreateCustomerContactDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Contact).NotNull().SetValidator(new ContactDtoValidator());
        }
    }

    public sealed class UpdateCustomerContactDtoValidator : AbstractValidator<UpdateCustomerContactDto>
    {
        public UpdateCustomerContactDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.ContactId).NotEmpty();
            RuleFor(x => x.Contact).NotNull().SetValidator(new ContactDtoValidator());
        }
    }

    public sealed class DeleteCustomerContactDtoValidator : AbstractValidator<DeleteCustomerContactDto>
    {
        public DeleteCustomerContactDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.ContactId).NotEmpty();
        }
    }

    public sealed class SetPrimaryCustomerContactDtoValidator : AbstractValidator<SetPrimaryCustomerContactDto>
    {
        public SetPrimaryCustomerContactDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.ContactId).NotEmpty();
        }
    }
}
