using System.ComponentModel.DataAnnotations;
using static WitcherHub.Infrastructure.Data.Models.Enums;

using WitcherHub.Domain.Commen;
namespace WitcherHub.Infrastructure.Data.Models
{
    public class Customer : BaseEntity
    {
        public CustomerType Type { get; set; } = CustomerType.Individual;

        [MaxLength(250)]
        public string Name { get; set; } = default!;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? TaxId { get; set; }

        public string? Notes { get; set; }

        public LexwareType LexwareType { get; set; }
        [MaxLength(64)] 
        public string? LexwareContactId { get; set; }
        [MaxLength(64)] 
        public string? LexwareOrganizationId { get; set; }
        
        public int? LexwareCustomerNumber { get; set; }
        public int? LexwareVersion { get; set; }
        public bool? LexwareArchived { get; set; }        
        public bool? LexwareAllowTaxFreeInvoices { get; set; }
        public DateTime? LexwareSyncedAtUtc { get; set; }
        public ICollection<CustomerEmailAddress> EmailAddresses { get; set; } = new List<CustomerEmailAddress>();

        public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
        public ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();

        public ICollection<Project> Projects { get; set; } = new List<Project>();
    }

    public class CustomerAddress : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Customer Customer { get; set; } = default!;
        [MaxLength(250)]
        public string FullNameOrCompany { get; set; } = default!; // اسم صاحب العنوان
        [MaxLength(300)]
        public string? StreetRaw { get; set; }


        [MaxLength(50)]
        public string? Label { get; set; } // Billing/Shipping

        [MaxLength(100)]
        public string? Country { get; set; }
        [MaxLength(2)]
        public string? CountryCode { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }


        [MaxLength(250)]
        public string? AddressLine2 { get; set; }

        [MaxLength(30)]
        public string? PostalCode { get; set; }

        public bool IsDefault { get; set; }
        public bool IsLexware { get; set; }
    }

    public class CustomerContact : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Customer Customer { get; set; } = default!;

        [MaxLength(200)]
        public string Name { get; set; } = default!;

        [MaxLength(320)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? Position { get; set; }
        [MaxLength(20)]
        public string? Salutation { get; set; }

        [MaxLength(120)]
        public string? FirstName { get; set; }

        [MaxLength(120)]
        public string? LastName { get; set; }

        public bool IsPrimary { get; set; }
        public bool IsLexware { get; set; }
    }
    public class CustomerEmailAddress : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Customer Customer { get; set; } = default!;

        [MaxLength(30)]
        public string Kind { get; set; } = "business"; // business / private / other

        [MaxLength(320)]
        public string Email { get; set; } = default!;
    }

}
