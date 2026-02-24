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

            // 2️⃣ Build persistence DTO
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
                    TermsStructured = generated.Structured
                },
                Items = generated.Structured.Positions
                    .OrderBy(p => p.PositionNo)
                    .Select(p => new ContractItemDto
                    {
                        Title = p.Title,
                        AgreedPrice = p.LineNetPrice,
                        Position = p.PositionNo,
                        Config = JsonDocument.Parse("{}")
                    })
                    .ToList()
            };

            // 3️⃣ Save to DB
            var contractId = await _contractManager.CreateAsync(dto, ct);

            return contractId;
        }
    }
}