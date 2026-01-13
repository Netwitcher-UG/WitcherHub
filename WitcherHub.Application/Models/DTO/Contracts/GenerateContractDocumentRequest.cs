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
    }

    public class ContractServiceLineDto
    {
        public int Position { get; set; } = 1;
        public string Title { get; set; } = default!;

        public string? ServiceName { get; set; }
        public string? ServiceType { get; set; }
        public string? PricingModel { get; set; }

        public decimal? AgreedPrice { get; set; }

        // config متغير (بديل JsonDocument لأنه أسهل للسواغر)
        public Dictionary<string, object> Config { get; set; } = new();
    }

    public class GenerateContractDocumentResponse
    {
        public string FixedTerms { get; set; } = default!;
        public string ServicesSection { get; set; } = default!;
        public string FullDocument { get; set; } = default!;
    }
}
