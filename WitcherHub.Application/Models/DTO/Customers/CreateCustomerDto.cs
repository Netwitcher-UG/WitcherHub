
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Customers
{
    public class CreateCustomerDto
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
        public string? AddressLine1 { get; set; }
        public string? PostalCode { get; set; }
        public string? AddressLine2 { get; set; }
    }

    public class ContactDto
    {
        public string? Name { get; set; }
        public string? Position { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }
}
