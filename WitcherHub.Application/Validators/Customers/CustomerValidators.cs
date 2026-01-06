using FluentValidation;
using WitcherHub.Application.Models.DTO.Customers;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Validators.Customers
{
    public sealed class CustomerDTOValidator : AbstractValidator<CustomerDTOs>
    {
        public CustomerDTOValidator()
        {
            RuleFor(x => x.Customer)
                .NotNull()
                .SetValidator(new CustomerDtoValidator());

            RuleFor(x => x.Address)
                .NotNull()
                .SetValidator(new AddressDtoValidator());

            // Company: contact required + stronger rules
            When(x => x.Customer.Type == CustomerType.Company, () =>
            {
                RuleFor(x => x.Contact)
                    .NotNull()
                    .SetValidator(new ContactDtoValidator());

                RuleFor(x => x.Contact).Custom((c, ctx) =>
                {
                    if (c is null) return;

                    var hasName =
                        !string.IsNullOrWhiteSpace(c.Name) ||
                        (!string.IsNullOrWhiteSpace(c.FirstName) && !string.IsNullOrWhiteSpace(c.LastName));

                    if (!hasName)
                    {
                        ctx.AddFailure("Contact.Name", "Contact name is required (or first/last name).");
                        ctx.AddFailure("Contact.FirstName", "First name is required when contact name is empty.");
                        ctx.AddFailure("Contact.LastName", "Last name is required when contact name is empty.");
                    }

                    var hasComms = !string.IsNullOrWhiteSpace(c.Email) || !string.IsNullOrWhiteSpace(c.Phone);
                    if (!hasComms)
                    {
                        ctx.AddFailure("Contact.Email", "Company contact must include at least email or phone.");
                        ctx.AddFailure("Contact.Phone", "Company contact must include at least email or phone.");
                    }
                });
            });

            // Individual: validate contact only if has any input (اختياري)
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

    // ============================
    // Customer
    // ============================

    public sealed class CustomerDtoValidator : AbstractValidator<CustomerDto>
    {
        private static readonly HashSet<string> AllowedKinds =
            new(StringComparer.OrdinalIgnoreCase) { "business", "private", "other" };

        public CustomerDtoValidator()
        {
            RuleFor(x => x.Type).IsInEnum();

            // Company vs Individual rules
            When(x => x.Type == CustomerType.Company, () =>
            {
                RuleFor(x => x.Name)
                    .NotEmpty().WithMessage("Company name is required.")
                    .MaximumLength(250);
            });

            When(x => x.Type == CustomerType.Individual, () =>
            {
                RuleFor(x => x.FirstName)
                    .NotEmpty().WithMessage("First name is required.")
                    .MaximumLength(120);

                RuleFor(x => x.LastName)
                    .NotEmpty().WithMessage("Last name is required.")
                    .MaximumLength(120);

                RuleFor(x => x.Name)
                    .MaximumLength(250);
            });

            RuleFor(x => x.Phone)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Phone));

            RuleFor(x => x.TaxId)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.TaxId));

            // EmailAddresses required
            RuleFor(x => x.EmailAddresses)
                .NotNull()
                .Must(list => list.Count > 0)
                .WithMessage("At least one email address is required.");

            RuleForEach(x => x.EmailAddresses)
                .SetValidator(new EmailAddressDtoValidator(AllowedKinds));
        }
    }

    public sealed class EmailAddressDtoValidator : AbstractValidator<EmailAddressDto>
    {
        private readonly ISet<string> _allowedKinds;

        public EmailAddressDtoValidator(ISet<string> allowedKinds)
        {
            _allowedKinds = allowedKinds;

            RuleFor(x => x.Kind)
                .NotEmpty().WithMessage("Email kind is required.")
                .MaximumLength(30)
                .Must(k => _allowedKinds.Contains((k ?? "").Trim()))
                .WithMessage("Email kind must be business, private, or other.");

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

    // ============================
    // Address
    // ============================

    public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
    {
        public AddressDtoValidator()
        {
            RuleFor(x => x.Label)
                .MaximumLength(50)
                .When(x => !string.IsNullOrWhiteSpace(x.Label));

            RuleFor(x => x.FullNameOrCompany)
                .MaximumLength(250)
                .When(x => !string.IsNullOrWhiteSpace(x.FullNameOrCompany));

            RuleFor(x => x.Country)
                .MaximumLength(100)
                .When(x => !string.IsNullOrWhiteSpace(x.Country));

            RuleFor(x => x.CountryCode)
                .MaximumLength(2)
                .When(x => !string.IsNullOrWhiteSpace(x.CountryCode));

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

            RuleFor(x => x).Custom((a, ctx) =>
            {
                var hasCountry = !string.IsNullOrWhiteSpace(a.Country);
                var hasCode = !string.IsNullOrWhiteSpace(a.CountryCode);

                if (!hasCountry && !hasCode)
                {
                    ctx.AddFailure(nameof(AddressDto.Country), "Country or CountryCode is required.");
                    ctx.AddFailure(nameof(AddressDto.CountryCode), "Country or CountryCode is required.");
                }
            });
        }
    }

    // ============================
    // Contact
    // ============================

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
    // Address ops
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
    // Contact ops
    // ============================

    public sealed class CreateCustomerContactDtoValidator : AbstractValidator<CreateCustomerContactDto>
    {
        public CreateCustomerContactDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Contact).NotNull().SetValidator(new ContactDtoValidator());

            RuleFor(x => x.Contact).Custom((c, ctx) =>
            {
                if (c is null) return;

                var hasName =
                    !string.IsNullOrWhiteSpace(c.Name) ||
                    (!string.IsNullOrWhiteSpace(c.FirstName) && !string.IsNullOrWhiteSpace(c.LastName));

                if (!hasName)
                {
                    ctx.AddFailure("Contact.Name", "Contact name is required (or first/last name).");
                    ctx.AddFailure("Contact.FirstName", "First name is required when contact name is empty.");
                    ctx.AddFailure("Contact.LastName", "Last name is required when contact name is empty.");
                }

                var hasComms = !string.IsNullOrWhiteSpace(c.Email) || !string.IsNullOrWhiteSpace(c.Phone);
                if (!hasComms)
                {
                    ctx.AddFailure("Contact.Email", "Email or phone is required.");
                    ctx.AddFailure("Contact.Phone", "Email or phone is required.");
                }
            });
        }
    }

    public sealed class UpdateCustomerContactDtoValidator : AbstractValidator<UpdateCustomerContactDto>
    {
        public UpdateCustomerContactDtoValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.ContactId).NotEmpty();
            RuleFor(x => x.Contact).NotNull().SetValidator(new ContactDtoValidator());

            RuleFor(x => x.Contact).Custom((c, ctx) =>
            {
                if (c is null) return;

                var hasName =
                    !string.IsNullOrWhiteSpace(c.Name) ||
                    (!string.IsNullOrWhiteSpace(c.FirstName) && !string.IsNullOrWhiteSpace(c.LastName));

                if (!hasName)
                {
                    ctx.AddFailure("Contact.Name", "Contact name is required (or first/last name).");
                    ctx.AddFailure("Contact.FirstName", "First name is required when contact name is empty.");
                    ctx.AddFailure("Contact.LastName", "Last name is required when contact name is empty.");
                }

                var hasComms = !string.IsNullOrWhiteSpace(c.Email) || !string.IsNullOrWhiteSpace(c.Phone);
                if (!hasComms)
                {
                    ctx.AddFailure("Contact.Email", "Email or phone is required.");
                    ctx.AddFailure("Contact.Phone", "Email or phone is required.");
                }
            });
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
