using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class Projects : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Customer Customer { get; set; } = default!;

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        public string? Description { get; set; }

        public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }

        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }

        public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
        public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
        public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();

    }
}
