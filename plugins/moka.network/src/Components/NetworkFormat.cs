using System.Globalization;

namespace Moka.Network.Components;

// One way of printing a round trip and a count for every view, so a table, a tile and a chart agree.
internal static class NetworkFormat
{
	public static string Milliseconds(double milliseconds) => milliseconds < 10
		? milliseconds.ToString("0.0", CultureInfo.CurrentCulture) + " ms"
		: milliseconds.ToString("N0", CultureInfo.CurrentCulture) + " ms";

	public static string Milliseconds(TimeSpan elapsed) => Milliseconds(elapsed.TotalMilliseconds);

	public static string Milliseconds(double? milliseconds) => milliseconds is { } value ? Milliseconds(value) : "-";

	public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

	public static string Plural(long count, string noun) => count == 1 ? $"1 {noun}" : $"{Count(count)} {noun}s";

	public static string Percent(double value) =>
		value.ToString(value is 0 or 100 ? "0" : "0.#", CultureInfo.CurrentCulture) + "%";

	// A chart's axis has no room for the resolver's address, only its name.
	public static string ResolverName(string label) => label.StartsWith("This machine", StringComparison.Ordinal)
		? "This machine"
		: label.Split(' ', 2)[0];
}
