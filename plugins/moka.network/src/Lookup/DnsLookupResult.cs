namespace Moka.Network.Lookup;

/// <summary>A lookup's answer, and what happened with the servers that were tried before the one that answered.</summary>
public sealed record DnsLookupResult(DnsExchange Exchange, IReadOnlyList<string> EarlierFailures);
