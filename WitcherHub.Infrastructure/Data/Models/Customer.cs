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

        [MaxLength(320)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? TaxId { get; set; }

        public string? Notes { get; set; }

        public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
        public ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();

        public ICollection<Project> Projects { get; set; } = new List<Project>();
    }

    public class CustomerAddress : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Customer Customer { get; set; } = default!;

        public string FullNameOrCompany { get; set; } = default!; // اسم صاحب العنوان

        public string Street { get; set; } = default!;
        public string StreetNr { get; set; } = default!; // رقم البيت


        [MaxLength(50)]
        public string? Label { get; set; } // Billing/Shipping

        [MaxLength(100)]
        public string? Country { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }


        [MaxLength(250)]
        public string? AddressLine2 { get; set; }

        [MaxLength(30)]
        public string? PostalCode { get; set; }

        public bool IsDefault { get; set; }
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

        public bool IsPrimary { get; set; }
    }

}
