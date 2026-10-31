using Moka.Network.Lookup;

namespace Moka.Network.Tests.Lookup;

/// <summary>What the picker offers and what the view prints for a record type, by its number.</summary>
public sealed class DnsNamesTests
{
	[Fact]
	public void Offered_ShowsEveryTypeByNameInThePickerOrder()
	{
		DnsRecordType[] offered = [.. DnsNames.Offered];

		Assert.Equal(
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
				DnsRecordType.DS,
				DnsRecordType.DNSKEY,
				DnsRecordType.PTR,
			],
			offered);
	}

	[Fact]
	public void Offered_NamesEveryTypeItShows()
	{
		foreach (DnsRecordType type in DnsNames.Offered)
		{
			Assert.Equal(type.ToString(), DnsNames.TypeName((int)type));
		}
	}

	[Theory]
	[InlineData(43, "DS")]
	[InlineData(48, "DNSKEY")]
	[InlineData(46, "RRSIG")]
	[InlineData(41, "OPT")]
	[InlineData(64, "SVCB")]
	[InlineData(99, "TYPE99")]
	public void TypeName_KnowsTheDnssecTypesAndTheRest(int type, string expected) => Assert.Equal(expected, DnsNames.TypeName(type));
}
