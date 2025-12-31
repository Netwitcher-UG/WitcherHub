using WitcherHub.Application.Models.DTO.Customers;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Customers
{
    public class CustomerViews
    {
        public class CustomerListItemView
        {
            public Guid Id { get; set; }
            public CustomerType Type { get; set; }
            public string Name { get; set; } = "";

            public string? Email { get; set; }

            public string? Phone { get; set; }
            public string? TaxId { get; set; }
            public string? City { get; set; }
            public LexwareType LexwareType { get; set; }
        }

        public class CustomerDetailsView
        {
            public Guid Id { get; set; }
            public CustomerType Type { get; set; }
            public string Name { get; set; } = "";

            public string? Email { get; set; }

            public string? Phone { get; set; }
            public string? TaxId { get; set; }
            public string? Notes { get; set; }
            public LexwareType LexwareType { get; set; }

            // ✅ Lexware read-only fields
            public int? LexwareCustomerNumber { get; set; }
            public string? LexwareContactId { get; set; }
            public string? LexwareOrganizationId { get; set; }
            public int? LexwareVersion { get; set; }
            public bool? LexwareArchived { get; set; }
            public bool? LexwareAllowTaxFreeInvoices { get; set; }
            public DateTime? LexwareSyncedAtUtc { get; set; }

            public List<CustomerEmailAddressItemView> EmailAddresses { get; set; } = new();
            public List<CustomerAddressItemView> Addresses { get; set; } = new();
            public List<CustomerContactItemView> Contacts { get; set; } = new();
        }


        public class CustomerEmailAddressItemView : EmailAddressDto
        {
            public Guid Id { get; set; }
        }

        public class CustomerAddressItemView : AddressDto
        {
            public Guid Id { get; set; }
        }

        public class CustomerContactItemView : ContactDto
        {
            public Guid Id { get; set; }
        }
    }
}
