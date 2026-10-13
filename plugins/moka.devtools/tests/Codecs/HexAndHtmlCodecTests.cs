using Moka.DevTools.Codecs;

namespace Moka.DevTools.Tests.Codecs;

public sealed class HexAndHtmlCodecTests
{
	[Fact]
	public void Hex_EncodesWithSeparatorsAndCase()
	{
		byte[] bytes = [0xDE, 0xAD, 0xBE, 0xEF];

		Assert.Equal("deadbeef", HexCodec.Encode(bytes, upper: false, HexSeparator.None));
		Assert.Equal("DE AD BE EF", HexCodec.Encode(bytes, upper: true, HexSeparator.Space));
		Assert.Equal("de:ad:be:ef", HexCodec.Encode(bytes, upper: false, HexSeparator.Colon));
	}

	[Theory]
	[InlineData("deadbeef")]
	[InlineData("DE AD be-ef")]
	[InlineData("de:ad:be:ef")]
	[InlineData("0xde 0xad 0xbe 0xef")]
	[InlineData("0xDEADBEEF")]
	[InlineData("\\xde\\xad\\xbe\\xef")]
	[InlineData("de, ad, be, ef\n")]
	public void Hex_DecodesTheFormsPeoplePaste(string text) => Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], HexCodec.Decode(text));

	[Fact]
	public void Hex_RefusesOddDigitsAndOtherCharacters()
	{
		Assert.Contains("one hex digit", Assert.Throws<CodecException>(() => HexCodec.Decode("dea")).Message, StringComparison.Ordinal);
		Assert.Contains("one hex digit", Assert.Throws<CodecException>(() => HexCodec.Decode("d ea")).Message, StringComparison.Ordinal);
		CodecException error = Assert.Throws<CodecException>(() => HexCodec.Decode("de zz"));
		Assert.Equal(3, error.Position);
		Assert.Contains("'z' at character 4", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Html_EncodesMarkupAndOptionallyEverythingElse()
	{
		Assert.Equal("&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt; é", HtmlCodec.Encode("<a href=\"x\">'&'</a> é", encodeNonAscii: false));
		Assert.Equal("&#xE9;&#x1F600;", HtmlCodec.Encode("é😀", encodeNonAscii: true));
	}

	[Fact]
	public void Html_DecodesNumericAndNamedReferences()
	{
		(string text, string? note) = HtmlCodec.Decode("&lt;b&gt;&#233;&#xE9;&#x1F600;&amp;&copy;&apos;&nbsp;");

		Assert.Equal("<b>éé😀&©'" + (char)0xA0, text);
		Assert.Null(note);
	}

	[Fact]
	public void Html_LeavesBareAmpersandsAsTheyAre() =>
		Assert.Equal("fish & chips &copy &#; ok", HtmlCodec.Decode("fish & chips &copy &#; ok").Text);

	[Theory]
	[InlineData("a &bogus; b", "&bogus;")]
	[InlineData("&#0;", "not a character")]
	[InlineData("&#xD800;", "not a character")]
	[InlineData("&#x110000;", "not a character")]
	[InlineData("&#123456789;", "too large")]
	public void Html_RefusesWhatItCannotDecode(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<CodecException>(() => HtmlCodec.Decode(text)).Message, StringComparison.Ordinal);

	[Fact]
	public void Html_NotesAMissingSemicolon()
	{
		(string text, string? note) = HtmlCodec.Decode("&#65x");

		Assert.Equal("Ax", text);
		Assert.NotNull(note);
	}

	[Fact]
	public void Html_RoundTrips()
	{
		string text = "<p class=\"x\">Tom & Jerry's \"café\" 😀</p>";

		Assert.Equal(text, HtmlCodec.Decode(HtmlCodec.Encode(text, encodeNonAscii: true)).Text);
		Assert.Equal(text, HtmlCodec.Decode(HtmlCodec.Encode(text, encodeNonAscii: false)).Text);
	}

	[Fact]
	public void Runner_RefusesWhatItCannotTake()
	{
		Assert.Contains("not UTF-8", Assert.Throws<CodecException>(() => CodecRunner.Run(new CodecRequest
		{
			Kind = CodecKind.Url,
			Direction = CodecDirection.Encode,
			FileBytes = [0xFF, 0xFE],
		})).Message, StringComparison.Ordinal);

		Assert.Contains("half of a character pair", Assert.Throws<CodecException>(() => CodecRunner.Run(new CodecRequest
		{
			Kind = CodecKind.Base64,
			Direction = CodecDirection.Encode,
			Text = "a" + (char)0xD800 + "b",
		})).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Runner_EncodesFileBytesAndDecodesToBytes()
	{
		CodecOutput encoded = CodecRunner.Run(new CodecRequest { Kind = CodecKind.Base64, Direction = CodecDirection.Encode, FileBytes = [0xFF, 0x00] });
		CodecOutput decoded = CodecRunner.Run(new CodecRequest { Kind = CodecKind.Base64, Direction = CodecDirection.Decode, Text = encoded.Text! });

		Assert.Equal("/wA=", encoded.Text);
		Assert.Equal([0xFF, 0x00], decoded.Bytes);
		Assert.DoesNotContain("/wA=", new CodecRequest { Text = "/wA=" }.ToString(), StringComparison.Ordinal);
	}
}
