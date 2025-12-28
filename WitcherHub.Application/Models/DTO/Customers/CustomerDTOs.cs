
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Customers
{
    public class CustomerDTOs
    {
        public CustomerDto Customer { get; set; } = new();
        public AddressDto Address { get; set; } = new();
        public ContactDto Contact { get; set; } = new();
    }

    public class CustomerDto
    {
        public CustomerType Type { get; set; } = CustomerType.Individual;
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? TaxId { get; set; }
        public string? Notes { get; set; }
    }

    public class AddressDto
    {
        public string? Label { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? FullNameOrCompany { get; set; }
        public string? Street { get; set; }
        public string? StreetNr { get; set; }

        public bool IsDefault { get; set; }
    }

    public class ContactDto
    {
        public string? Name { get; set; }
        public string? Position { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool IsPrimary { get; set; }
    }
    public class UpdateCustomerDto
    {
        public CustomerDTOs Customer { get; set; } = new();
    }

    // ---------- Addresses ----------
    public class CreateCustomerAddressDto
    {
        public Guid CustomerId { get; set; }
        public AddressDto Address { get; set; } = new();
    }

    public class UpdateCustomerAddressDto
    {
        public Guid CustomerId { get; set; }
        public Guid AddressId { get; set; }
        public AddressDto Address { get; set; } = new();
    }

    public class DeleteCustomerAddressDto
    {
        public Guid CustomerId { get; set; }
        public Guid AddressId { get; set; }
    }

    public class SetDefaultCustomerAddressDto
    {
        public Guid CustomerId { get; set; }
        public Guid AddressId { get; set; }
    }

    // ---------- Contacts ----------
    public class CreateCustomerContactDto
    {
        public Guid CustomerId { get; set; }
        public ContactDto Contact { get; set; } = new();
    }

    public class UpdateCustomerContactDto
    {
        public Guid CustomerId { get; set; }
        public Guid ContactId { get; set; }
        public ContactDto Contact { get; set; } = new();
    }

    public class DeleteCustomerContactDto
    {
        public Guid CustomerId { get; set; }
        public Guid ContactId { get; set; }
    }

    public class SetPrimaryCustomerContactDto
    {
        public Guid CustomerId { get; set; }
        public Guid ContactId { get; set; }
    }


    // view
    public class CustomerListItemDto
    {
        public Guid Id { get; set; }
        public CustomerType Type { get; set; }
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? TaxId { get; set; }
        public string? City { get; set; } // غالباً default address city
    }

    public class CustomerDetailsDto
    {
        public Guid Id { get; set; }
        public CustomerType Type { get; set; }
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? TaxId { get; set; }
        public string? Notes { get; set; }

        public List<CustomerAddressItemDto> Addresses { get; set; } = new();
        public List<CustomerContactItemDto> Contacts { get; set; } = new();
    }

    public class CustomerAddressItemDto : AddressDto
    {
        public Guid Id { get; set; }
    }

    public class CustomerContactItemDto : ContactDto
    {
        public Guid Id { get; set; }
    }


}
