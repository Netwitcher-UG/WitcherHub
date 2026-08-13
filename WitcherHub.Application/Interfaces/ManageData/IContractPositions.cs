using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Interfaces.ManageData
{
    /// <summary>
    /// Contract positions that may or may not come from the Service Catalog.
    ///
    /// Separate from <see cref="IContract"/>, which keeps its existing
    /// catalog-driven item operations working untouched.
    /// </summary>
    public interface IContractPositions
    {
        Task<IReadOnlyList<ManualPositionDto>> GetPositionsAsync(Guid contractId, CancellationToken ct = default);

        /// <summary>
        /// Replaces the contract's positions with the supplied set, in the order
        /// given. Totals are recalculated server-side and a snapshot of the agreed
        /// terms is stored with each position.
        /// </summary>
        Task<PositionTotalsDto> SavePositionsAsync(
            Guid contractId,
            IReadOnlyList<ManualPositionDto> positions,
            CancellationToken ct = default);

        /// <summary>
        /// Totals for a set of positions without saving, for the live summary.
        /// </summary>
        PositionTotalsDto CalculateTotals(IReadOnlyList<ManualPositionDto> positions, string fallbackCurrency = "EUR");
    }
}
