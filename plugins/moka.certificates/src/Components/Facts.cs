using Microsoft.AspNetCore.Components;
using Moka.Certificates.Crypto;
using Moka.Red.Primitives.DataList;

namespace Moka.Certificates.Components;

/// <summary>The rows of a <see cref="FactCard"/>: plain text, or data drawn by <see cref="FactValue"/>.</summary>
internal static class Facts
{
	private static readonly Dictionary<string, string> NamePartLabels = new(StringComparer.Ordinal)
	{
		["CN"] = "Common name",
		["O"] = "Organization",
		["OU"] = "Unit",
		["L"] = "City",
		["ST"] = "State",
		["C"] = "Country",
		["E"] = "Email",
		["DC"] = "Domain part",
		["SN"] = "Surname",
		["GN"] = "Given name",
		["UID"] = "User id",
	};

	public static MokaDataListItem Text(string label, string value) => new(label, value);

	public static MokaDataListItem Mono(string label, string value, string? copyLabel = null) => new(label, ValueContent: Value(value, mono: true, copyLabel));

	/// <summary>A distinguished name a row per part, in the order it is written: CN before O before C.</summary>
	public static IReadOnlyList<MokaDataListItem> NameParts(IReadOnlyList<NamePart> parts) => parts.Count == 0
		? [Text("Name", "Empty")]
		: [.. parts.Select(part => Mono(NamePartLabels.TryGetValue(part.Label, out string? label) ? label : part.Label, part.Value))];

	/// <summary>The subject alternative names, a row per name under its kind: DNS, IP, email, URI.</summary>
	public static IReadOnlyList<MokaDataListItem> Names(IReadOnlyList<GeneralNameInfo> names) => names.Count == 0
		? [Text("Names", "None")]
		: [.. names.Select(name => Mono(name.Tag, name.Value))];

	public static RenderFragment Value(string text, bool mono, string? copyLabel = null) => builder =>
	{
		builder.OpenComponent<FactValue>(0);
		builder.AddComponentParameter(1, nameof(FactValue.Text), text);
		builder.AddComponentParameter(2, nameof(FactValue.Mono), mono);
		builder.AddComponentParameter(3, nameof(FactValue.CopyLabel), copyLabel);
		builder.CloseComponent();
	};
}
