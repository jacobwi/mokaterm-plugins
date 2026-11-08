namespace Moka.Network.Components;

/// <summary>
/// A row and its place in the list, which keys it in a table: a DNS answer can hold the same record twice and a
/// connection table two equal rows, and a repeated key would make Blazor throw.
/// </summary>
public sealed record IndexedRow<T>(int Index, T Item);

public static class IndexedRows
{
	public static IReadOnlyList<IndexedRow<T>> Of<T>(IEnumerable<T> items) => [.. items.Select((item, index) => new IndexedRow<T>(index, item))];
}
