namespace Moka.Network.Subnets;

/// <summary>What an address is for, by the special-purpose range it falls in (IANA's registries), and that range.</summary>
public sealed record AddressKind(string Name, string Range, int RangePrefix, bool IsPublic, string? Note = null);
