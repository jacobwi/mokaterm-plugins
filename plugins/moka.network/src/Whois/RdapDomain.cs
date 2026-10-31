namespace Moka.Network.Whois;

/// <summary>
/// The parts of an RDAP domain answer worth showing: the name as the registry writes it, its statuses, the dates of its
/// events, the registrar and the nameservers. Dates stay as the server wrote them, so nothing here is guessed at.
/// </summary>
public sealed record RdapDomain
{
	/// <summary>The domain's name in letters and digits (ldhName); empty when the answer is no domain record.</summary>
	public required string Name { get; init; }

	public string? Handle { get; init; }

	public IReadOnlyList<string> Statuses { get; init; } = [];

	/// <summary>The event dates RDAP names: registration, last change and expiration, when the answer carries them.</summary>
	public string? Registered { get; init; }

	public string? Updated { get; init; }

	public string? Expires { get; init; }

	/// <summary>The registrar's own name, from the registrar entity's vCard; null when the answer gives none.</summary>
	public string? Registrar { get; init; }

	/// <summary>The registrar's identifier, the IANA number most registries attach to that entity.</summary>
	public string? RegistrarId { get; init; }

	public IReadOnlyList<string> Nameservers { get; init; } = [];
}
