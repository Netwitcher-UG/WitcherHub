using System;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Services;
using WitcherHub.Application.Models.View.Services;

namespace WitcherHub.Application.Interfaces.ManageData
{
    public interface IServiceCatalog
    {
        Task<PagedResult<ServiceViews.ServiceListItemView>> GetServicesAsync(
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default);

        Task<ServiceViews.ServiceDetailsView?> GetServiceAsync(Guid id, CancellationToken ct = default);
        Task<List<ServiceViews.ServiceListItemView>> GetServiceLookupAsync(CancellationToken ct = default);
        Task<Guid> CreateAsync(ServiceCatalogDTOs dto, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateServiceCatalogItemDto dto, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        // PricingRules
        Task<Guid> CreateRuleAsync(CreatePricingRuleDto dto, CancellationToken ct = default);
        Task UpdateRuleAsync(UpdatePricingRuleDto dto, CancellationToken ct = default);
        Task DeleteRuleAsync(DeletePricingRuleDto dto, CancellationToken ct = default);
    }
}
