using System.ComponentModel.DataAnnotations;
using static WitcherHub.Infrastructure.Data.Models.Enums;

using WitcherHub.Domain.Commen;
namespace WitcherHub.Infrastructure.Data.Models
{
    public class Project : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Customer Customer { get; set; } = default!;

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        public string? Description { get; set; }

        /// <summary>
        /// The project's own status. Only a person changes this.
        ///
        /// Quote and contract code used to overwrite it — creating a draft
        /// contract set the project to Waiting — so the value shown on one screen
        /// was not the value another screen had, and the delete rule keyed on it
        /// refused projects that were nothing but a title.
        /// </summary>
        public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

        /// <summary>
        /// When the project was archived, or null while it is active.
        ///
        /// Archiving is the ordinary way to get a project out of the way:
        /// everything in it — quotes, contracts, invoices, payments — is kept
        /// exactly as it was and can still be found. Permanent deletion is a
        /// separate, restricted act.
        /// </summary>
        public DateTimeOffset? ArchivedAt { get; set; }

        public Guid? ArchivedById { get; set; }

        public bool IsArchived => ArchivedAt.HasValue;

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
