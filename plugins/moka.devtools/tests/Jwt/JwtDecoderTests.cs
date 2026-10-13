using System.Text.Json;
using Moka.DevTools.Jwt;

namespace Moka.DevTools.Tests.Jwt;

public sealed class JwtDecoderTests
{
	[Fact]
	public void Decode_ReadsTheJwtIoToken()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.JwtIo);

		Assert.Equal("HS256", token.Algorithm);
		Assert.Equal("JWT", token.Type);
		Assert.Equal(["sub", "name", "iat"], token.Claims.Select(claim => claim.Name));
		Assert.Equal("\"John Doe\"", token.Claims[1].Json);
		Assert.Equal(JsonValueKind.Number, token.Claims[2].Kind);
		JwtTime issued = Assert.Single(token.Times);
		Assert.Equal("iat", issued.Claim);
		Assert.Equal(new DateTimeOffset(2018, 1, 18, 1, 30, 22, TimeSpan.Zero), issued.When);
		Assert.Equal(32, token.Signature.Length);
		Assert.Equal("SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", token.SignatureText);
		Assert.Contains("\"alg\": \"HS256\"", token.HeaderJson, StringComparison.Ordinal);
		Assert.Empty(token.Notes);
	}

	[Fact]
	public void Decode_TakesABearerHeaderAndWrappedLines()
	{
		string wrapped = "Bearer " + TestTokens.JwtIo[..40] + "\n  " + TestTokens.JwtIo[40..];

		JwtToken token = JwtDecoder.Decode(wrapped);

		Assert.Equal("HS256", token.Algorithm);
		Assert.Contains(token.Notes, note => note.Contains("line breaks", StringComparison.Ordinal));
	}

	[Fact]
	public void Decode_ReadsEveryTimeClaim()
	{
		string token = $"{TestTokens.Part("""{"alg":"none"}""")}.{TestTokens.Part("""{"exp":1700000000.5,"nbf":0,"iat":1,"auth_time":"yesterday"}""")}.";

		JwtToken read = JwtDecoder.Decode(token);

		Assert.Equal(["exp", "nbf", "iat", "auth_time"], read.Times.Select(time => time.Claim));
		Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_500), read.Times[0].When);
		Assert.Equal(DateTimeOffset.UnixEpoch, read.Times[1].When);
		Assert.Null(read.Times[3].When);
		Assert.Contains("a string", read.Times[3].Problem, StringComparison.Ordinal);
		Assert.True(read.IsUnsigned);
		Assert.Empty(read.Signature);
	}

	[Fact]
	public void Decode_RefusesAnEncryptedToken()
	{
		string header = TestTokens.Part("""{"alg":"RSA-OAEP","enc":"A256GCM"}""");
		string jwe = $"{header}.ZW5jcnlwdGVka2V5.aXY.Y2lwaGVydGV4dA.dGFn";

		JwtFormatException refusal = Assert.Throws<JwtFormatException>(() => JwtDecoder.Decode(jwe));

		Assert.Contains("encrypted", refusal.Message, StringComparison.Ordinal);
		Assert.Contains("RSA-OAEP", refusal.Message, StringComparison.Ordinal);
		Assert.Contains("A256GCM", refusal.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("abc", "this has 1")]
	[InlineData("a.b", "this has 2")]
	[InlineData("a.b.c.d", "this has 4")]
	public void Decode_RefusesTheWrongNumberOfParts(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<JwtFormatException>(() => JwtDecoder.Decode(text)).Message, StringComparison.Ordinal);

	[Fact]
	public void Decode_SaysWhichPartIsNotBase64urlAndWhere()
	{
		string[] parts = TestTokens.JwtIo.Split('.');
		string broken = $"{parts[0]}.{parts[1][..5]}*{parts[1][6..]}.{parts[2]}";

		string message = Assert.Throws<JwtFormatException>(() => JwtDecoder.Decode(broken)).Message;

		Assert.Contains("payload", message, StringComparison.Ordinal);
		Assert.Contains("character 6", message, StringComparison.Ordinal);
	}

	[Fact]
	public void Decode_PointsAtStandardBase64()
	{
		string token = $"{TestTokens.Part("""{"alg":"none"}""")}.ab+/.";

		Assert.Contains("standard Base64", Assert.Throws<JwtFormatException>(() => JwtDecoder.Decode(token)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Decode_SaysWhereTheHeaderJsonBreaks()
	{
		string token = $"{TestTokens.Part("{\"alg\":\n  \"HS256\",,}")}.{TestTokens.Part("{}")}.";

		string message = Assert.Throws<JwtFormatException>(() => JwtDecoder.Decode(token)).Message;

		Assert.Contains("header is not JSON", message, StringComparison.Ordinal);
		Assert.Contains("line 2", message, StringComparison.Ordinal);
	}

	[Fact]
	public void Decode_ShowsAPayloadThatIsNotJson()
	{
		string token = $"{TestTokens.Part("""{"alg":"HS256"}""")}.{TestTokens.Part("plain text")}.c2ln";

		JwtToken read = JwtDecoder.Decode(token);

		Assert.Null(read.PayloadJson);
		Assert.Equal("plain text", read.PayloadText);
		Assert.Empty(read.Claims);
		Assert.Contains(read.Notes, note => note.Contains("not JSON", StringComparison.Ordinal));
	}

	[Fact]
	public void Decode_NotesPaddingAndAMissingAlgorithm()
	{
		string token = $"{TestTokens.Part("""{"typ":"JWT"}""")}.{TestTokens.Part("{\"a\":1}")}==.";

		JwtToken read = JwtDecoder.Decode(token);

		Assert.Null(read.Algorithm);
		Assert.Contains(read.Notes, note => note.Contains("no algorithm", StringComparison.Ordinal));
		Assert.Contains(read.Notes, note => note.Contains("padded", StringComparison.Ordinal));
	}

	[Fact]
	public void ToString_PrintsNoClaimValues()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.JwtIo);

		Assert.DoesNotContain("John", token.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("1234567890", token.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("John", token.Claims[1].ToString(), StringComparison.Ordinal);
		Assert.Contains("name", token.Claims[1].ToString(), StringComparison.Ordinal);
	}
}
