using System.Globalization;

namespace Moka.Network.Lookup;

/// <summary>What the view prints for record types and response codes, by their number, known or not.</summary>
public static class DnsNames
{
	/// <summary>The types the lookup offers, in the order the picker shows them.</summary>
	public static readonly IReadOnlyList<DnsRecordType> Offered =
	[
		DnsRecordType.A,
		DnsRecordType.AAAA,
		DnsRecordType.CNAME,
		DnsRecordType.MX,
		DnsRecordType.NS,
		DnsRecordType.TXT,
		DnsRecordType.SOA,
		DnsRecordType.SRV,
		DnsRecordType.CAA,
		DnsRecordType.PTR,
	];

	/// <summary>A type's mnemonic, or the RFC 3597 form (<c>TYPE65</c>) for one this plugin has no name for.</summary>
	public static string TypeName(int type) => type switch
	{
		1 => "A",
		2 => "NS",
		5 => "CNAME",
		6 => "SOA",
		12 => "PTR",
		15 => "MX",
		16 => "TXT",
		28 => "AAAA",
		33 => "SRV",
		39 => "DNAME",
		41 => "OPT",
		43 => "DS",
		46 => "RRSIG",
		47 => "NSEC",
		48 => "DNSKEY",
		50 => "NSEC3",
		64 => "SVCB",
		65 => "HTTPS",
		257 => "CAA",
		_ => string.Create(CultureInfo.InvariantCulture, $"TYPE{type}"),
	};

	/// <summary>A class's mnemonic; everything here is IN, but a hostile reply can say anything.</summary>
	public static string ClassName(int recordClass) => recordClass switch
	{
		1 => "IN",
		3 => "CH",
		4 => "HS",
		_ => string.Create(CultureInfo.InvariantCulture, $"CLASS{recordClass}"),
	};

	/// <summary>The response code's name, extended codes from an OPT record included.</summary>
	public static string ResponseCodeName(int code) => code switch
	{
		0 => "NOERROR",
		1 => "FORMERR",
		2 => "SERVFAIL",
		3 => "NXDOMAIN",
		4 => "NOTIMP",
		5 => "REFUSED",
		6 => "YXDOMAIN",
		7 => "YXRRSET",
		8 => "NXRRSET",
		9 => "NOTAUTH",
		10 => "NOTZONE",
		16 => "BADVERS",
		23 => "BADCOOKIE",
		_ => string.Create(CultureInfo.InvariantCulture, $"RCODE{code}"),
	};

	/// <summary>What a response code means, in a sentence, for the codes people meet.</summary>
	public static string? ResponseCodeMeaning(int code) => code switch
	{
		0 => null,
		1 => "The server could not read the query.",
		2 => "The server failed to get an answer. With a validating resolver this is often a DNSSEC failure.",
		3 => "The name does not exist.",
		4 => "The server does not support this kind of query.",
		5 => "The server refused to answer. It may only answer its own network or its own zones.",
		16 => "The server does not speak this EDNS version.",
		_ => null,
	};
}
