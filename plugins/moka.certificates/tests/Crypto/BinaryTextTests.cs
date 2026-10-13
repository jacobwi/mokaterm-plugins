using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class BinaryTextTests
{
	[Fact]
	public void Format_AndParse_RoundTripBothEncodings()
	{
		byte[] bytes = [0x00, 0x01, 0xAB, 0xFF, 0x10];

		Assert.True(BinaryText.TryParse(BinaryText.Format(bytes, BinaryEncoding.Hex), out byte[]? fromHex));
		Assert.True(BinaryText.TryParse(BinaryText.Format(bytes, BinaryEncoding.Base64), out byte[]? fromBase64));

		Assert.Equal(bytes, fromHex);
		Assert.Equal(bytes, fromBase64);
	}

	[Fact]
	public void TryParse_IgnoresColonsSpacesAndLineBreaks()
	{
		Assert.True(BinaryText.TryParse("AB:CD\r\n 01", out byte[]? bytes));

		Assert.Equal(new byte[] { 0xAB, 0xCD, 0x01 }, bytes);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not base64!")]
	[InlineData("abc")]
	public void TryParse_RefusesWhatIsNeither(string text) => Assert.False(BinaryText.TryParse(text, out _));

	[Fact]
	public void Colons_IsUpperCaseHexPairs() => Assert.Equal("0A:FF:10", BinaryText.Colons([0x0A, 0xFF, 0x10]));
}
