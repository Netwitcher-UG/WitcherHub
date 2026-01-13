using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Interfaces
{
    public interface IContractDocumentGenerator
    {
        Task<GenerateContractDocumentResponse> GenerateAsync(
            GenerateContractDocumentRequest request,
            CancellationToken ct = default);
    }
}
