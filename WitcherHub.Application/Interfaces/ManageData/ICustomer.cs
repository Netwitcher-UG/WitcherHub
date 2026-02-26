
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Application.Models.View.Customers;

namespace WitcherHub.Application.Interfaces.ManageData
{
    public interface ICustomer
    {
        Task<PagedResult<CustomerViews.CustomerListItemView>> GetCustomersAsync(int page = 1, int pageSize = 10, string? search = null, CancellationToken ct = default);
        Task<CustomerViews.CustomerDetailsView?> GetCustomerAsync(Guid id, CancellationToken ct = default);

        Task<Guid> CreateAsync(CustomerDTOs dto, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateCustomerDto dto, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        Task<Guid> CreateAddressAsync(CreateCustomerAddressDto dto, CancellationToken ct = default);
        Task UpdateAddressAsync(UpdateCustomerAddressDto dto, CancellationToken ct = default);
        Task DeleteAddressAsync(DeleteCustomerAddressDto dto, CancellationToken ct = default);
        Task SetDefaultAddressAsync(SetDefaultCustomerAddressDto dto, CancellationToken ct = default);

        Task<Guid> CreateContactAsync(CreateCustomerContactDto dto, CancellationToken ct = default);
        Task UpdateContactAsync(UpdateCustomerContactDto dto, CancellationToken ct = default);
        Task DeleteContactAsync(DeleteCustomerContactDto dto, CancellationToken ct = default);
        Task SetPrimaryContactAsync(SetPrimaryCustomerContactDto dto, CancellationToken ct = default);
        Task<List<CustomerProjectItemView>> GetCustomerProjectsAsync(Guid customerId, CancellationToken ct = default);
    }
}
