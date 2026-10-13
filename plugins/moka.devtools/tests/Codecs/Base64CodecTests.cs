using System.Text;
using Moka.DevTools.Codecs;

namespace Moka.DevTools.Tests.Codecs;

public sealed class Base64CodecTests
{
	[Fact]
	public void Encode_WritesBothAlphabetsWithAndWithoutPadding()
	{
		byte[] hello = Encoding.UTF8.GetBytes("Hello, world!");
		byte[] high = [0xFB, 0xFF];

		Assert.Equal("SGVsbG8sIHdvcmxkIQ==", Base64Codec.Encode(hello, urlSafe: false, padding: true));
		Assert.Equal("SGVsbG8sIHdvcmxkIQ", Base64Codec.Encode(hello, urlSafe: false, padding: false));
		Assert.Equal("+/8=", Base64Codec.Encode(high, urlSafe: false, padding: true));
		Assert.Equal("-_8", Base64Codec.Encode(high, urlSafe: true, padding: false));
	}

	[Theory]
	[InlineData(false, true)]
	[InlineData(false, false)]
	[InlineData(true, true)]
	[InlineData(true, false)]
	public void Decode_RoundTripsEveryLength(bool urlSafe, bool padding)
	{
		Random random = new(7);
		for (int length = 0; length < 70; length++)
		{
			byte[] data = new byte[length];
			random.NextBytes(data);

			(byte[] bytes, string? note) = Base64Codec.Decode(Base64Codec.Encode(data, urlSafe, padding), urlSafe);

			Assert.Equal(data, bytes);
			Assert.Null(note);
		}
	}

	[Fact]
	public void Decode_SkipsWhitespaceAndTakesMissingPadding()
	{
		Assert.Equal("Hello, world!", Encoding.UTF8.GetString(Base64Codec.Decode("SGVsbG8s\r\n  IHdvcmxk\tIQ", urlSafe: false).Bytes));
	}

	[Fact]
	public void Decode_NamesTheCharacterAndItsPlace()
	{
		CodecException error = Assert.Throws<CodecException>(() => Base64Codec.Decode("SGVs%G8s", urlSafe: false));

		Assert.Equal(4, error.Position);
		Assert.Contains("'%' at character 5", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Decode_PointsAtTheOtherAlphabet()
	{
		Assert.Contains("looks like Base64url", Assert.Throws<CodecException>(() => Base64Codec.Decode("-_8", urlSafe: false)).Message, StringComparison.Ordinal);
		Assert.Contains("looks like standard Base64", Assert.Throws<CodecException>(() => Base64Codec.Decode("+/8", urlSafe: true)).Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("SGVs=G8s", "only come at the end")]
	[InlineData("SGVsb", "single character")]
	[InlineData("SGVs=", "after a complete group")]
	[InlineData("SGVsbG=", "padding does not fit")]
	public void Decode_RefusesBrokenShapes(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<CodecException>(() => Base64Codec.Decode(text, urlSafe: false)).Message, StringComparison.Ordinal);

	[Fact]
	public void Decode_NotesBitsPastTheEnd()
	{
		(byte[] bytes, string? note) = Base64Codec.Decode("QR==", urlSafe: false);

		Assert.Equal([0x41], bytes);
		Assert.NotNull(note);
	}
}
