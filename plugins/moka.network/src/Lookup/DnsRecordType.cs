using System.Diagnostics.CodeAnalysis;

namespace Moka.Network.Lookup;

/// <summary>The record types the lookup can ask for, plus OPT, which only ever arrives in the additional section.</summary>
public enum DnsRecordType
{
	A = 1,
	NS = 2,
	CNAME = 5,
	SOA = 6,
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "PTR is the record type's own name in RFC 1035, which every DNS tool prints.")]
	PTR = 12,
	MX = 15,
	TXT = 16,
	AAAA = 28,
	SRV = 33,
	OPT = 41,
	CAA = 257,
}
