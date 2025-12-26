using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using WitcherHub.Domain.Commen;
using static WitcherHub.Infrastructure.Data.Models.Enums;


namespace WitcherHub.Infrastructure.Data.Models
{

    public class Attachment : BaseEntity
    {
        public AttachmentOwnerType OwnerType { get; set; }
        public Guid OwnerId { get; set; } // polymorphic (بدون FK)

        [MaxLength(255)]
        public string FileName { get; set; } = default!;

        [MaxLength(120)]
        public string? MimeType { get; set; }

        public long? FileSizeBytes { get; set; }

        [MaxLength(500)]
        public string StorageKey { get; set; } = default!; // S3 key/local path

        [MaxLength(200)]
        public string? Checksum { get; set; }

        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument? Meta { get; set; }
    }

    public class AuditLog : BaseEntity
    {
        public Guid? ActorUserId { get; set; }
        public AppUser? ActorUser { get; set; }

        [MaxLength(60)]
        public string EntityType { get; set; } = default!; // "invoice" ...

        public Guid EntityId { get; set; }

        [MaxLength(60)]
        public string Action { get; set; } = default!; // CREATE/UPDATE/ISSUE...

        [Column(TypeName = "jsonb")]
        public JsonDocument? BeforeData { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument? AfterData { get; set; }

        [MaxLength(60)]
        public string? IpAddress { get; set; }
    }
}
