using Moka.Kubernetes.Server;

namespace Moka.Kubernetes.Tests.Server;

/// <summary>The quoting every kubectl word passes through, with the hostile names a pod could carry in a hostile world.</summary>
public sealed class ServerQuoteTests
{
	[Theory]
	[InlineData("kubectl", "'kubectl'")]
	[InlineData("/usr/local/bin/kubectl", "'/usr/local/bin/kubectl'")]
	[InlineData("web pod", "'web pod'")]
	[InlineData("web; rm -rf /", "'web; rm -rf /'")]
	[InlineData("web$(id)", "'web$(id)'")]
	[InlineData("web`id`", "'web`id`'")]
	[InlineData("web*.log", "'web*.log'")]
	[InlineData("pod's pod", "'pod'\\''s pod'")]
	[InlineData(" démarré ", "' démarré '")]
	public void Quote_PutsOneWordOnTheLineUntouched(string value, string expected)
	{
		Assert.Equal(expected, ServerQuote.Quote(value));
	}

	[Fact]
	public void Quote_RefusesANulCharacter()
	{
		Assert.Throws<ArgumentException>(() => ServerQuote.Quote("web\0pod"));
	}

	[Fact]
	public void Line_JoinsTheQuotedWordsWithSingleSpaces()
	{
		Assert.Equal("'get' 'pods' '--no-headers'", ServerQuote.Line(["get", "pods", "--no-headers"]));
	}

	[Fact]
	public void Line_QuotesEveryWordEvenTheFlags()
	{
		Assert.Equal("'kubectl' '-n' 'production' 'get' 'pods'", ServerQuote.Line(["kubectl", "-n", "production", "get", "pods"]));
	}
}
