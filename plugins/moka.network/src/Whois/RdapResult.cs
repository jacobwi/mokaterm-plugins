namespace Moka.Network.Whois;

/// <summary>
/// One domain asked of the registries over RDAP: the registry's answer, the registrar's own when its referral was
/// followed, and both bodies as they came, so what the servers said stays one click away.
/// </summary>
public sealed record RdapResult
{
	/// <summary>The domain asked for, as the query carried it.</summary>
	public required string Query { get; init; }

	/// <summary>The registry's RDAP server that answered, as a host name.</summary>
	public required string Server { get; init; }

	/// <summary>The registrar's RDAP server, when the answer referred to one and it was asked.</summary>
	public string? RegistrarServer { get; init; }

	/// <summary>The domain's facts, with the registrar's own answer merged in when it added one.</summary>
	public required RdapDomain Domain { get; init; }

	/// <summary>The registry's answer, exactly as it came.</summary>
	public required string RegistryJson { get; init; }

	/// <summary>The registrar's answer, when it was asked.</summary>
	public string? RegistrarJson { get; init; }

	/// <summary>Why the registrar's server gave no answer; the registry's stands.</summary>
	public string? Note { get; init; }
}
