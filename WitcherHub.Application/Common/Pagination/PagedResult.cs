
namespace WitcherHub.Application.Common.Pagination
{
    public sealed class PagedResult<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }      // 1-based
        public required int PageSize { get; init; }

        public required long TotalItems { get; init; }

        public int TotalPages => PageSize <= 0
            ? 0
            : (int)((TotalItems + PageSize - 1) / PageSize);

        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;

        public int FromItem => TotalItems == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public long ToItem => TotalItems == 0 ? 0 : Math.Min((long)Page * PageSize, TotalItems);

        public static PagedResult<T> Empty(int page, int pageSize)
            => new()
            {
                Items = Array.Empty<T>(),
                Page = page,
                PageSize = pageSize,
                TotalItems = 0
            };
    }
}
