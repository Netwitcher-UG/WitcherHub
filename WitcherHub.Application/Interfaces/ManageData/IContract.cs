using System;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Interfaces.ManageData
{
    public interface IContract
    {
        Task<PagedResult<ContractViews.ContractListItemView>> GetContractsByProjectAsync(
            Guid projectId,
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default);

        Task<ContractViews.ContractDetailsView?> GetContractAsync(Guid id, CancellationToken ct = default);

        Task<Guid> CreateAsync(ContractDTOs dto, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateContractDto dto, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        // Items ops
        Task<Guid> CreateItemAsync(CreateContractItemDto dto, CancellationToken ct = default);
        Task UpdateItemAsync(UpdateContractItemDto dto, CancellationToken ct = default);
        Task DeleteItemAsync(DeleteContractItemDto dto, CancellationToken ct = default);
        Task ReorderItemsAsync(ReorderContractItemsDto dto, CancellationToken ct = default);
        Task UpdateHeaderAsync(Guid contractId,
    DocumentStatus status,
    DateOnly? startDate,
    DateOnly? endDate,
    string? terms,
    InvoiceSendMode invoiceSendMode,
    CancellationToken ct = default);
    }
}
