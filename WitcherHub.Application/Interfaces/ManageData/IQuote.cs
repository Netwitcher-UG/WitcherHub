using System;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.View.Quotes;

namespace WitcherHub.Application.Interfaces.ManageData
{
    public interface IQuote
    {
        Task<PagedResult<QuoteViews.QuoteListItemView>> GetQuotesByProjectAsync(
            Guid projectId,
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default);

        Task<QuoteViews.QuoteDetailsView?> GetQuoteAsync(Guid id, CancellationToken ct = default);

        Task<Guid> CreateAsync(QuoteDTOs dto, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateQuoteDto dto, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        // Items ops
        Task<Guid> CreateItemAsync(CreateQuoteItemDto dto, CancellationToken ct = default);
        Task UpdateItemAsync(UpdateQuoteItemDto dto, CancellationToken ct = default);
        Task DeleteItemAsync(DeleteQuoteItemDto dto, CancellationToken ct = default);
        Task ReorderItemsAsync(ReorderQuoteItemsDto dto, CancellationToken ct = default);
    }
}
