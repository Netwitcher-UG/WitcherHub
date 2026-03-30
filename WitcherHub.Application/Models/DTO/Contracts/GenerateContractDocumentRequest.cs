using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Contracts
{
    public class GenerateContractDocumentRequest
    {
        // بيانات عامة
        public string? ContractNo { get; set; }          // اختياري (يمكن placeholder)
        public string ProjectTitle { get; set; } = "Demo Project";
        public string Currency { get; set; } = "EUR";
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }

        // تأكيد من المستخدم
        public string SignerName { get; set; } = default!;
        public string? SignerEmail { get; set; }

        // هل نترك بيانات العميل فارغة؟
        public bool LeaveCustomerFieldsBlank { get; set; } = true;

        // هل نظهر الأسعار داخل قسم الخدمات؟
        public bool IncludePricesInServicesSection { get; set; } = false;

        // ملاحظات إضافية للـ GPT
        public string? AdditionalInstructions { get; set; }

        // قائمة الخدمات (يدوي أو من DB)
        public List<ContractServiceLineDto> Services { get; set; } = new();

        // (اختياري) لو بدك تمرر بلوك عميل جاهز بدل placeholder
        public string? CustomerBlockOverride { get; set; }
        public Guid ProjectId { get; set; }
        public ContractStructuredTermsDto? StructuredOverride { get; set; }
    }

    public class ContractServiceLineDto
    {
        public int Position { get; set; } = 1;
        public string Title { get; set; } = default!;

        public string? ServiceName { get; set; }
        public string? ServiceType { get; set; }
        public string? PricingModel { get; set; }
        public Guid? ServiceId { get; set; }
        public decimal? AgreedPrice { get; set; }
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; } = 0m;
        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;

        public DiscountType? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }
        // config متغير (بديل JsonDocument لأنه أسهل للسواغر)
        public Dictionary<string, object> Config { get; set; } = new();
    }

    public class GenerateContractDocumentResponse
    {
        // ✅ Structured Anlage A (الأساس الجديد)
        public ContractStructuredTermsDto Structured { get; set; } = new();

        // ✅ Markdown مولد من Structured (للعرض/الطباعة مؤقتاً)
        public string ServicesSectionMarkdown { get; set; } = default!;

        // ✅ المستند الكامل بعد الدمج مع التمبلت
        public string FullDocument { get; set; } = default!;
    }
}
