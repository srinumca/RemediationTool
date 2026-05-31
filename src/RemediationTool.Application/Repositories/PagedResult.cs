namespace RemediationTool.Application.Repositories;

/// <summary>
/// Wraps a single page of query results with a cursor for fetching the next page.
///
/// Compatible with both DynamoDB (where NextPageKey is the serialised LastEvaluatedKey token)
/// and the JSON file implementation (where NextPageKey is a zero-based skip-count string).
///
/// When NextPageKey is null, there are no further pages.
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>The records on this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>
    /// Cursor to pass as lastEvaluatedKey in the next query call.
    /// Null when this is the final page.
    /// </summary>
    public string? NextPageKey { get; init; }

    /// <summary>Total number of items on this page.</summary>
    public int Count => Items.Count;

    /// <summary>True when this is the final page (no further data available).</summary>
    public bool IsLastPage => NextPageKey is null;
}