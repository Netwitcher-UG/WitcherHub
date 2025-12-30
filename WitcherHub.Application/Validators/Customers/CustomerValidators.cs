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

            // Company: contact required
            When(x => x.Customer.Type == CustomerType.Company, () =>
            {
                RuleFor(x => x.Contact)
                    .NotNull()
                    .SetValidator(new ContactDtoValidator());

                RuleFor(x => x.Contact)
                    .Must(c =>
                        !string.IsNullOrWhiteSpace(c.Name) ||
                        (!string.IsNullOrWhiteSpace(c.FirstName) && !string.IsNullOrWhiteSpace(c.LastName)))
                    .WithMessage("Contact name (or first/last name) is required for company.");

                RuleFor(x => x.Contact)
                    .Must(c => !string.IsNullOrWhiteSpace(c.Email) || !string.IsNullOrWhiteSpace(c.Phone))
                    .WithMessage("Company contact must include at least email or phone.");
            });

            // Individual: validate contact only if has any input
            When(x => x.Customer.Type != CustomerType.Company, () =>
            {
                When(x => !IsEmptyContact(x.Contact), () =>
                {
                    RuleFor(x => x.Contact).SetValidator(new ContactDtoValidator());
                });
            });
        }

        private static bool IsEmptyContact(ContactDto c)
        {
            if (c is null) return true;

            return string.IsNullOrWhiteSpace(c.Name)
                && string.IsNullOrWhiteSpace(c.Email)
                && string.IsNullOrWhiteSpace(c.Phone)
                && string.IsNullOrWhiteSpace(c.Position)
                && string.IsNullOrWhiteSpace(c.Salutation)
                && string.IsNullOrWhiteSpace(c.FirstName)
                && string.IsNullOrWhiteSpace(c.LastName);
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
    // Base validators
    // ============================

    public sealed class CustomerDtoValidator : AbstractValidator<CustomerDto>
    {
        public CustomerDtoValidator()
        {
            RuleFor(x => x.Type).IsInEnum();

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(250);

            RuleFor(x => x.Phone)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Phone));

            RuleFor(x => x.TaxId)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.TaxId));

            // ✅ EmailAddresses بدل Email
            RuleFor(x => x.EmailAddresses)
                .NotNull()
                .Must(list => list.Count > 0)
                .WithMessage("At least one email address is required.");

            RuleForEach(x => x.EmailAddresses)
                .SetValidator(new EmailAddressDtoValidator());
        }
    }

    public sealed class EmailAddressDtoValidator : AbstractValidator<EmailAddressDto>
    {
        public EmailAddressDtoValidator()
        {
            RuleFor(x => x.Kind)
                .NotEmpty().WithMessage("Email kind is required.")
                .MaximumLength(30);

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .MaximumLength(320)
                .EmailAddress().WithMessage("Invalid email.");
        }
    }

    public sealed class UpdateBasicRequestValidator : AbstractValidator<UpdateBasicRequest>
    {
        public UpdateBasicRequestValidator()
        {
            RuleFor(x => x.CustomerId)
                .NotEmpty().WithMessage("CustomerId is required.");

            RuleFor(x => x.Customer)
                .NotNull().WithMessage("Customer is required.")
                .SetValidator(new CustomerDtoValidator());
        }
    }

    public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
    {
        public AddressDtoValidator()
        {
            RuleFor(x => x.Label)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Label));

            // ✅ FullNameOrCompany required (لأن الـ Entity non-null)
            RuleFor(x => x.FullNameOrCompany)
                .NotEmpty().WithMessage("Full name / company is required.")
                .MaximumLength(250);

            RuleFor(x => x.Country)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.Country));

            RuleFor(x => x.CountryCode)
                .MaximumLength(2)
                .When(x => !string.IsNullOrWhiteSpace(x.CountryCode));

            // مطلوب واحد على الأقل من Country أو CountryCode
            RuleFor(x => x)
                .Must(a => !string.IsNullOrWhiteSpace(a.CountryCode) || !string.IsNullOrWhiteSpace(a.Country))
                .WithMessage("Country or CountryCode is required.");

            RuleFor(x => x.City)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.City));

            RuleFor(x => x.PostalCode)
                .MaximumLength(30)
                .When(x => !string.IsNullOrWhiteSpace(x.PostalCode));

            RuleFor(x => x.StreetRaw)
                .MaximumLength(300)
                .When(x => !string.IsNullOrWhiteSpace(x.StreetRaw));

            RuleFor(x => x.AddressLine2)
                .MaximumLength(250)
                .When(x => !string.IsNullOrWhiteSpace(x.AddressLine2));
        }
    }

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

            RuleFor(x => x.Salutation)
                .MaximumLength(20)
                .When(x => !string.IsNullOrWhiteSpace(x.Salutation));

            RuleFor(x => x.FirstName)
                .MaximumLength(120)
                .When(x => !string.IsNullOrWhiteSpace(x.FirstName));

            RuleFor(x => x.LastName)
                .MaximumLength(120)
                .When(x => !string.IsNullOrWhiteSpace(x.LastName));
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
