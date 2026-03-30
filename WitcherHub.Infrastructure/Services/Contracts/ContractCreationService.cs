using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    public class ContractCreationService
    {
        private readonly IContractDocumentGenerator _generator;
        private readonly IContract _contractManager;

        public ContractCreationService(
            IContractDocumentGenerator generator,
            IContract contractManager)
        {
            _generator = generator;
            _contractManager = contractManager;
        }


        public async Task<Guid> GenerateAndCreateAsync(
    GenerateContractDocumentRequest request,
    CancellationToken ct = default)
        {
            if (request.ProjectId == Guid.Empty)
                throw new InvalidOperationException("ProjectId is required.");

            // 1️⃣ Generate contract (AI)
            var generated = await _generator.GenerateAsync(request, ct);

            // 2️⃣ Build persistence DTO with Data Linking
            var dto = new ContractDTOs
            {
                Contract = new ContractDto
                {
                    ProjectId = request.ProjectId,
                    Currency = request.Currency,
                    Status = DocumentStatus.Draft,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Terms = generated.FullDocument,
                    FromQuote = true,
                    TermsStructured = generated.Structured
                },
                Items = (request.Services ?? new List<ContractServiceLineDto>())
    .OrderBy(s => s.Position)
    .Select(s => new ContractItemDto
    {
        ServiceId = s.ServiceId,
        Title = s.Title, // أو العنوان من الـ AI إذا كنت تفضله
        Quantity = s.Quantity <= 0 ? 1m : s.Quantity,
        UnitPrice = s.UnitPrice,
        BillingCycle = s.BillingCycle,
        DiscountType = s.DiscountType,
        DiscountValue = s.DiscountValue,
        AgreedPrice = s.AgreedPrice,
    
        Position = s.Position,
        Config = s.Config is not null
            ? JsonDocument.Parse(JsonSerializer.Serialize(s.Config))
            : JsonDocument.Parse("{}")
    })
    .ToList()
            };

            // 3️⃣ Save to DB
            var contractId = await _contractManager.CreateAsync(dto, ct);

            return contractId;
        }
    }
}
