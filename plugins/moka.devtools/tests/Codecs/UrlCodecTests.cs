using System.Text;
using Moka.DevTools.Codecs;
using Moka.DevTools.Text;

namespace Moka.DevTools.Tests.Codecs;

public sealed class UrlCodecTests
{
	[Fact]
	public void Encode_ComponentEscapesAllButUnreserved() =>
		Assert.Equal("a%20b%26c%2Fd%3F%C3%A9-._~", UrlCodec.Encode("a b&c/d?é-._~", UrlMode.Component));

	[Fact]
	public void Encode_WholeUrlKeepsItsStructure() =>
		Assert.Equal("https://x.example/a%20b?q=1&r=%C3%A9#top", UrlCodec.Encode("https://x.example/a b?q=1&r=é#top", UrlMode.WholeUrl));

	[Fact]
	public void Decode_ReadsUtf8AndPlusOnlyWhenAsked()
	{
		Assert.Equal("é+x", Encoding.UTF8.GetString(UrlCodec.Decode("%C3%a9+x", UrlMode.Component, plusIsSpace: false)));
		Assert.Equal("é x", Encoding.UTF8.GetString(UrlCodec.Decode("%C3%A9+x", UrlMode.Component, plusIsSpace: true)));
	}

	[Fact]
	public void Decode_WholeUrlLeavesEscapedStructureEscaped() =>
		Assert.Equal("/a b/%2F?x=%26", Encoding.UTF8.GetString(UrlCodec.Decode("/a%20b/%2F?x=%26", UrlMode.WholeUrl, plusIsSpace: false)));

	[Theory]
	[InlineData("ab%G1", 2)]
	[InlineData("ab%4", 2)]
	[InlineData("%", 0)]
	public void Decode_RefusesABrokenEscape(string text, int position)
	{
		CodecException error = Assert.Throws<CodecException>(() => UrlCodec.Decode(text, UrlMode.Component, plusIsSpace: false));

		Assert.Equal(position, error.Position);
	}

	[Fact]
	public void Decode_KeepsBytesThatAreNotUtf8()
	{
		byte[] bytes = UrlCodec.Decode("%FF%FEa", UrlMode.Component, plusIsSpace: false);

		Assert.Equal([0xFF, 0xFE, (byte)'a'], bytes);
		Assert.False(StrictUtf8.TryDecode(bytes, out _));
	}

	[Fact]
	public void Component_RoundTrips()
	{
		string text = "naïve café: 50% off & more / ok? 😀 [x]";

		Assert.Equal(text, Encoding.UTF8.GetString(UrlCodec.Decode(UrlCodec.Encode(text, UrlMode.Component), UrlMode.Component, plusIsSpace: false)));
	}
}
