using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static WitcherHub.Infrastructure.Data.Models.Enums;

using WitcherHub.Domain.Commen;
namespace WitcherHub.Infrastructure.Data.Models
{

    public class Milestone : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = default!;

        public Guid? ContractId { get; set; }
        public Contract? Contract { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        public string? Description { get; set; }

        public MilestoneStatus Status { get; set; } = MilestoneStatus.Planned;

        public DateOnly? DueDate { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal? TargetAmount { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public ICollection<MilestoneInvoice> InvoiceLinks { get; set; } = new List<MilestoneInvoice>();
    }

    public class MilestoneInvoice
    {
        public Guid MilestoneId { get; set; }
        public Milestone Milestone { get; set; } = default!;

        public Guid InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;
    }
}
