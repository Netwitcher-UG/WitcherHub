using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.View.Overview;
using WitcherHub.Application.Models.View.Registers;

namespace WitcherHub.Application.Interfaces.ManageData
{
    /// <summary>
    /// Reads quotes, contracts and invoices across the whole business.
    ///
    /// Separate from <see cref="IQuote"/>, <see cref="IContract"/> and
    /// <see cref="IInvoice"/>, which are all scoped to one project and own the
    /// write side. This interface is read-only and deliberately narrow: it exists
    /// so a document can be found without first knowing which project it belongs
    /// to, which was previously impossible.
    /// </summary>
    public interface IDocumentRegister
    {
        Task<PagedResult<QuoteRegisterRow>> GetQuotesAsync(RegisterFilter filter, CancellationToken ct = default);

        Task<PagedResult<ContractRegisterRow>> GetContractsAsync(RegisterFilter filter, CancellationToken ct = default);

        Task<PagedResult<InvoiceRegisterRow>> GetInvoicesAsync(RegisterFilter filter, CancellationToken ct = default);

        /// <summary>
        /// Customers that have at least one document of the given kind, for the
        /// filter dropdown. Listing every customer would offer choices that return
        /// nothing.
        /// </summary>
        Task<IReadOnlyList<(Guid Id, string Name)>> GetCustomersWithDocumentsAsync(
            DocumentKind kind,
            CancellationToken ct = default);

        Task<BusinessOverview> GetOverviewAsync(CancellationToken ct = default);
    }

    public enum DocumentKind
    {
        Quote,
        Contract,
        Invoice
    }
}
