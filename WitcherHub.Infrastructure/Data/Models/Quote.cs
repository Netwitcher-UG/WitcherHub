using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WitcherHub.Domain.Commen;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class Quote : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = default!;

        [MaxLength(50)]
        public string QuoteNo { get; set; } = default!;

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public string? Notes { get; set; }
        public QuoteAfterSignAction AfterCustomerSignAction { get; set; } = QuoteAfterSignAction.Contract;
        public InvoiceSendMode InvoiceSendMode { get; set; } = InvoiceSendMode.Automatic;
      
        // recurring مثل العقد
        public bool RecurringEnabled { get; set; } = false;
        public bool RecurringIsActive { get; set; } = false;
        public DateOnly? RecurringStartDate { get; set; }
        public DateOnly? RecurringEndDate { get; set; }
        public DateOnly? NextRecurringInvoiceDate { get; set; }
        public DateTimeOffset? LastRecurringInvoiceRunAt { get; set; }
        public DateTimeOffset? IssuedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool ApplyVat { get; set; } = true;
        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }
        public DateTimeOffset? SignedAt { get; set; }

       

        // signatures
        public ICollection<QuoteSignature> Signatures { get; set; } = new List<QuoteSignature>();
        public ICollection<QuoteAccessLink> AccessLinks { get; set; } = new List<QuoteAccessLink>();
        public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();
    }

    public class QuoteItem : BaseEntity
    {
        public Guid QuoteId { get; set; }
        public Quote Quote { get; set; } = default!;

        public Guid? ServiceId { get; set; }
        public ServiceCatalogItem? Service { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = default!;

    

        public ServiceUnitType UnitType { get; set; } = ServiceUnitType.Custom;

        [MaxLength(30)]
        public string UnitName { get; set; } = string.Empty;

        [Column(TypeName = "numeric(12,2)")]
        public decimal Quantity { get; set; } = 1m;

        [Column(TypeName = "numeric(12,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

        [Column(TypeName = "jsonb")]
        public JsonDocument? PriceBreakdown { get; set; }
        [MaxLength(500)]
        public string Description { get; set; } = null!;
        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;
        public DiscountType? DiscountType { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal? DiscountValue { get; set; }

        public int Position { get; set; } = 1;
    }
    public class QuoteSignature : BaseEntity
    {
        public Guid QuoteId { get; set; }
        public Quote Quote { get; set; } = default!;

        [MaxLength(200)]
        public string SignerName { get; set; } = default!;

        [MaxLength(320)]
        public string SignerEmail { get; set; } = default!;

        public DateTimeOffset? SignedAt { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument? SignatureData { get; set; }
    }
    public class QuoteAccessLink : BaseEntity
    {
        public Guid QuoteId { get; set; }
        public Quote Quote { get; set; } = default!;

        [MaxLength(128)]
        public string TokenHash { get; set; } = default!; // SHA256 hex

        [MaxLength(320)]
        public string RecipientEmail { get; set; } = default!;

        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? LastOpenedAtUtc { get; set; }
        public DateTimeOffset? RevokedAtUtc { get; set; }

        public bool IsRevoked => RevokedAtUtc != null;

        public static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
