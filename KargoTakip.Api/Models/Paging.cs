namespace KargoTakip.Api.Models;

/// <summary>What the list screen asks for: an optional status, a free-text search, a page, and "only open shipments without a courier".</summary>
public record ShipmentFilter(ShipmentStatus? Status, string? Search, int Page = 1, int PageSize = 10, bool Unassigned = false);

/// <summary>One page of results plus the numbers the UI needs to draw the pager.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, int TotalPages)
{
    public PagedResult<TOut> WithItems<TOut>(IReadOnlyList<TOut> items) => new(items, Total, Page, PageSize, TotalPages);
}
