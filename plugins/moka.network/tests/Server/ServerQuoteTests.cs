using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class ServerQuoteTests
{
	[Theory]
	[InlineData("example.com", "'example.com'")]
	[InlineData("10.0.0.1", "'10.0.0.1'")]
	[InlineData("", "''")]
	[InlineData("a b", "'a b'")]
	[InlineData("it's", "'it'\\''s'")]
	[InlineData("$HOME", "'$HOME'")]
	[InlineData("`id`", "'`id`'")]
	[InlineData("a;rm -rf /", "'a;rm -rf /'")]
	[InlineData("$(whoami)", "'$(whoami)'")]
	[InlineData("a|b&c>d", "'a|b&c>d'")]
	public void Quote_WrapsOneShellWordPlainly(string value, string quoted)
	{
		Assert.Equal(quoted, ServerQuote.Quote(value));
	}

	[Fact]
	public void Quote_LeavesNonAsciiTextAlone()
	{
		Assert.Equal("'exämple-ünicode.example'", ServerQuote.Quote("exämple-ünicode.example"));
	}

	[Fact]
	public void Quote_RefusesTheOneCharacterNoCommandLineCanCarry()
	{
		Assert.Throws<ArgumentException>(() => ServerQuote.Quote("a\0b"));
	}

	[Fact]
	public void Quote_ThrowsOnNull()
	{
		Assert.Throws<ArgumentNullException>(() => ServerQuote.Quote(null!));
	}

	[Fact]
	public void Line_QuotesAndJoinsEveryWord()
	{
		Assert.Equal("'getent' 'hosts' 'ex ample.com'", ServerQuote.Line(["getent", "hosts", "ex ample.com"]));
	}

	[Fact]
	public void Line_EmptyIsAnEmptyLine()
	{
		Assert.Equal("", ServerQuote.Line([]));
	}
}
