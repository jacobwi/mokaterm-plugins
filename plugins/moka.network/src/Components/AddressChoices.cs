using Moka.Network.Targets;
using Mokaterm.UI.Common.Components;

namespace Moka.Network.Components;

/// <summary>The address family picker that ping, the port check and traceroute share.</summary>
public static class AddressChoices
{
	public static readonly IReadOnlyList<EnumOption<AddressChoice>> Options =
	[
		new(AddressChoice.Any, "Any"),
		new(AddressChoice.IPv4, "IPv4"),
		new(AddressChoice.IPv6, "IPv6"),
	];
}
