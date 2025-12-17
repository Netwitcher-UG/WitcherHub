using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Data.Models
{

    public class Contract : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = default!;

        [MaxLength(50)]
        public string ContractNo { get; set; } = default!;

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft; // Signed/Terminated...

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public string? Terms { get; set; }

        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }

        public DateTimeOffset? SignedAt { get; set; }

        public ICollection<ContractItem> Items { get; set; } = new List<ContractItem>();
        public ICollection<ContractSignature> Signatures { get; set; } = new List<ContractSignature>();
    }

    public class ContractItem : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        [MaxLength(80)]
        public string? ServiceId { get; set; }
        public ServiceCatalogItem? Service { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        [Column(TypeName = "jsonb")]
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

        [Column(TypeName = "numeric(12,2)")]
        public decimal? AgreedPrice { get; set; }

        public int Position { get; set; } = 1;
    }

    public class ContractSignature : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        [MaxLength(200)]
        public string SignerName { get; set; } = default!;

        [MaxLength(320)]
        public string? SignerEmail { get; set; }

        public DateTimeOffset? SignedAt { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument? SignatureData { get; set; }
    }
}
