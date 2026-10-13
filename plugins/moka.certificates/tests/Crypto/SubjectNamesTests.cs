using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class SubjectNamesTests
{
	[Fact]
	public void Parse_TellsKindsApartByShapeAndPrefix()
	{
		(IReadOnlyList<GeneralNameInfo> names, IReadOnlyList<string> problems) = SubjectNames.Parse(
			"example.com, *.example.com\n192.0.2.1 2001:db8::1\nadmin@example.com\nhttps://example.com/x\nDNS:printer\nIP:10.0.0.1");

		Assert.Empty(problems);
		Assert.Equal(
			["DNS:example.com", "DNS:*.example.com", "IP:192.0.2.1", "IP:2001:db8::1", "email:admin@example.com", "URI:https://example.com/x", "DNS:printer", "IP:10.0.0.1"],
			names.Select(name => name.ToString()));
	}

	[Fact]
	public void Parse_InternationalNames_BecomeAscii()
	{
		(IReadOnlyList<GeneralNameInfo> names, _) = SubjectNames.Parse("bücher.example\nEXAMPLE.ORG.");

		Assert.Equal(["xn--bcher-kva.example", "example.org"], names.Select(name => name.Value));
	}

	[Fact]
	public void Parse_DropsDuplicates()
	{
		(IReadOnlyList<GeneralNameInfo> names, _) = SubjectNames.Parse("a.example, A.example, a.example");

		Assert.Single(names);
	}

	[Theory]
	[InlineData("foo..example")]
	[InlineData("-bad.example")]
	[InlineData("a.*.example")]
	[InlineData("under_score.example")]
	[InlineData("IP:300.1.1.1")]
	[InlineData("a@b@c")]
	[InlineData("URI:nota-uri")]
	public void Parse_RefusesWhatACertificateCannotHold(string entry)
	{
		(IReadOnlyList<GeneralNameInfo> names, IReadOnlyList<string> problems) = SubjectNames.Parse(entry);

		Assert.NotEmpty(problems);
		Assert.Empty(names);
	}
}
