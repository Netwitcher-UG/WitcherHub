using System;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Invoices;
using WitcherHub.Application.Models.View.Invoices;

namespace WitcherHub.Application.Interfaces.ManageData
{
    public interface IInvoice
    {
        Task<PagedResult<InvoiceViews.InvoiceListItemView>> GetInvoicesByProjectAsync(
            Guid projectId,
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default);

        Task<InvoiceViews.InvoiceDetailsView?> GetInvoiceAsync(Guid id, CancellationToken ct = default);

        Task<Guid> CreateAsync(InvoiceDTOs dto, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateInvoiceDto dto, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        // Items ops
        Task<Guid> CreateItemAsync(CreateInvoiceItemDto dto, CancellationToken ct = default);
        Task UpdateItemAsync(UpdateInvoiceItemDto dto, CancellationToken ct = default);
        Task DeleteItemAsync(DeleteInvoiceItemDto dto, CancellationToken ct = default);
        Task ReorderItemsAsync(ReorderInvoiceItemsDto dto, CancellationToken ct = default);
    }
}
