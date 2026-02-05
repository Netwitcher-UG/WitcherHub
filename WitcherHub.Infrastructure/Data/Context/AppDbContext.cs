
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Data.Context
{
    public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // -------- Customers --------
        public DbSet<Customer> Customers => Set<Customer>(); 
        public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
        public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
        public DbSet<CustomerEmailAddress> CustomerEmailAddresses => Set<CustomerEmailAddress>();

        // -------- Projects --------
        public DbSet<Project> Projects => Set<Project>();

        // -------- Services & Pricing --------
        public DbSet<ServiceCatalogItem> Services => Set<ServiceCatalogItem>();
        public DbSet<PricingRule> PricingRules => Set<PricingRule>();

        // -------- Taxes & Discounts --------
        public DbSet<TaxRate> TaxRates => Set<TaxRate>();
        public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();

        // -------- Quotes --------
        public DbSet<Quote> Quotes => Set<Quote>();
        public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();

        // -------- Contracts --------
        public DbSet<Contract> Contracts => Set<Contract>();
        public DbSet<ContractItem> ContractItems => Set<ContractItem>();
        public DbSet<ContractSignature> ContractSignatures => Set<ContractSignature>();

        // -------- Invoices --------
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
        public DbSet<InvoiceTotal> InvoiceTotals => Set<InvoiceTotal>();
        public DbSet<Payment> Payments => Set<Payment>();

        // -------- Milestones --------
        public DbSet<Milestone> Milestones => Set<Milestone>();
        public DbSet<MilestoneInvoice> MilestoneInvoices => Set<MilestoneInvoice>();

        // -------- Attachments & Audit --------
        public DbSet<Attachment> Attachments => Set<Attachment>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<ContractAccessLink> ContractAccessLinks => Set<ContractAccessLink>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // =========================
            // Primary Keys / Precision
            // =========================

            // ServiceCatalogItem uses string PK
            b.Entity<ServiceCatalogItem>().HasKey(x => x.Id);

            // InvoiceTotal: PK = InvoiceId (1-1)
            b.Entity<InvoiceTotal>().HasKey(x => x.InvoiceId);

            // MilestoneInvoice: composite key
            b.Entity<MilestoneInvoice>().HasKey(x => new { x.MilestoneId, x.InvoiceId });

            // (اختياري) دقة الأرقام المالية بشكل عام
            // لو أنت محددها بـ [Column(TypeName="numeric(12,2)")] داخل الموديلات ما تحتاج هذا.
            // بس تركتها لك كمرجع لو تحب توحّدها لاحقاً.


            // =========================
            // Relationships
            // =========================

            // Customer -> Addresses / Contacts
            b.Entity<CustomerAddress>()
                .HasOne(x => x.Customer)
                .WithMany(x => x.Addresses)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<CustomerContact>()
                .HasOne(x => x.Customer)
                .WithMany(x => x.Contacts)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Project -> Customer
            b.Entity<Project>()
                .HasOne(x => x.Customer)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quote -> Project
            b.Entity<Quote>()
                .HasOne(x => x.Project)
                .WithMany(x => x.Quotes)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // QuoteItem -> Quote
            b.Entity<QuoteItem>()
                .HasOne(x => x.Quote)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);

            // QuoteItem -> Service (اختياري)
            b.Entity<QuoteItem>()
                .HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.SetNull);

            // Contract -> Project
            b.Entity<Contract>()
                .HasOne(x => x.Project)
                .WithMany(x => x.Contracts)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // ContractItem -> Contract
            b.Entity<ContractItem>()
                .HasOne(x => x.Contract)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            // ContractItem -> Service (اختياري)
            b.Entity<ContractItem>()
                .HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.SetNull);

            // ContractSignature -> Contract
            b.Entity<ContractSignature>()
                .HasOne(x => x.Contract)
                .WithMany(x => x.Signatures)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            // Invoice -> Project
            b.Entity<Invoice>()
                .HasOne(x => x.Project)
                .WithMany(x => x.Invoices)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Invoice -> Contract (اختياري)
            b.Entity<Invoice>()
                .HasOne(x => x.Contract)
                .WithMany()
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.SetNull);

            // InvoiceItem -> Invoice
            b.Entity<InvoiceItem>()
                .HasOne(x => x.Invoice)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // InvoiceItem -> Service (اختياري)
            b.Entity<InvoiceItem>()
                .HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.SetNull);

            // InvoiceTotal -> Invoice (1-1)
            b.Entity<InvoiceTotal>()
                .HasOne(x => x.Invoice)
                .WithOne(x => x.Totals)
                .HasForeignKey<InvoiceTotal>(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Payment -> Invoice
            b.Entity<Payment>()
                .HasOne(x => x.Invoice)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Service -> PricingRules
            b.Entity<PricingRule>()
                .HasOne(x => x.Service)
                .WithMany(x => x.PricingRules)
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Milestone -> Project
            b.Entity<Milestone>()
                .HasOne(x => x.Project)
                .WithMany(x => x.Milestones)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Milestone -> Contract (اختياري)
            b.Entity<Milestone>()
                .HasOne(x => x.Contract)
                .WithMany()
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.SetNull);

            // MilestoneInvoice join
            b.Entity<MilestoneInvoice>()
                .HasOne(x => x.Milestone)
                .WithMany(x => x.InvoiceLinks)
                .HasForeignKey(x => x.MilestoneId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<MilestoneInvoice>()
                .HasOne(x => x.Invoice)
                .WithMany(x => x.MilestoneLinks)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // AuditLog -> AppUser (اختياري)
            b.Entity<AuditLog>()
                .HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Attachment -> AppUser (اختياري)
            b.Entity<Attachment>()
                .HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            b.Entity<CustomerEmailAddress>()
             .HasOne(x => x.Customer)
             .WithMany(x => x.EmailAddresses)
             .HasForeignKey(x => x.CustomerId)
             .OnDelete(DeleteBehavior.Cascade);


            // =========================
            // Indexes
            // =========================
            b.Entity<Customer>().HasIndex(x => x.Name);
            b.Entity<Project>().HasIndex(x => x.CustomerId);

            b.Entity<ServiceCatalogItem>().HasIndex(x => x.ServiceType);
            b.Entity<PricingRule>().HasIndex(x => new { x.ServiceId, x.Priority });

            b.Entity<Quote>().HasIndex(x => x.ProjectId);
            b.Entity<Invoice>().HasIndex(x => x.ProjectId);
            b.Entity<Invoice>().HasIndex(x => x.ContractId);

            b.Entity<Payment>().HasIndex(x => x.InvoiceId);
            b.Entity<Milestone>().HasIndex(x => x.ProjectId);

            b.Entity<Attachment>().HasIndex(x => new { x.OwnerType, x.OwnerId });
            b.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });
            b.Entity<ContractAccessLink>()
                .HasIndex(x => x.TokenHash)
                .IsUnique();

            b.Entity<ContractAccessLink>()
                .HasOne(x => x.Contract)
                .WithMany()
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<ContractAccessLink>()
                .HasIndex(x => new { x.ContractId, x.RecipientEmail });


            // =========================
            // Enums -> string (مهم)
            // =========================
            // هذا يخلي القيم تنحفظ بالنص بدل أرقام (أسهل للقراءة ولا يتكسر مع تغيير ترتيب enum)
            foreach (var entityType in b.Model.GetEntityTypes())
            {
                foreach (var prop in entityType.GetProperties())
                {
                    if (prop.ClrType.IsEnum)
                    {
                        prop.SetProviderClrType(typeof(string));
                    }
                }
            }
        }
    }
}
