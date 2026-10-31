using System.Text;
using Moka.Certificates.Crypto;
using Moka.Certificates.Tls;

namespace Moka.Certificates.Tests.Tls;

/// <summary>
/// The STARTTLS line exchange against a stream pair that only stands for a server: one side scripted with the lines
/// the server sends, the other collecting what the checker sends back, so every mode and refusal is replayed without
/// a network.
/// </summary>
public sealed class StartTlsExchangeTests
{
	[Theory]
	[InlineData(StartTlsMode.Smtp)]
	[InlineData(StartTlsMode.Imap)]
	[InlineData(StartTlsMode.Ftp)]
	public async Task Exchange_SpeaksEachModeAndAgreesToSwitch(StartTlsMode mode)
	{
		FakeServer server = new(GreetingOf(mode), EhloReply(mode), CommandReplyOf(mode));

		await StartTlsExchange.ExchangeAsync(mode, server.Stream, TestContext.Current.CancellationToken);

		Assert.Equal(ExpectedLines(mode).ToArray(), server.Sent);
	}

	[Fact]
	public async Task Exchange_PlainModeSpeaksNothing()
	{
		FakeServer server = new();

		await StartTlsExchange.ExchangeAsync(StartTlsMode.Plain, server.Stream, TestContext.Current.CancellationToken);

		Assert.Empty(server.Sent);
	}

	[Fact]
	public async Task Exchange_SmtpReadsTheMultilineEhloReply()
	{
		FakeServer server = new(
		[
			"220 mail.example.com ESMTP ready",
			"250-mail.example.com",
			"250-STARTTLS",
			"250 PIPELINING",
			"220 Ready to start TLS",
		]);

		await StartTlsExchange.ExchangeAsync(StartTlsMode.Smtp, server.Stream, TestContext.Current.CancellationToken);

		Assert.Equal(["EHLO mokaterm", "STARTTLS"], server.Sent);
	}

	[Fact]
	public async Task Exchange_ImapSkipsUntaggedLines()
	{
		FakeServer server = new(
		[
			"* OK [CAPABILITY IMAP4rev1 STARTTLS] ready",
			"* CAPABILITY IMAP4rev1 STARTTLS",
			"a001 OK Begin TLS negotiation now",
		]);

		await StartTlsExchange.ExchangeAsync(StartTlsMode.Imap, server.Stream, TestContext.Current.CancellationToken);

		Assert.Equal(["a001 STARTTLS"], server.Sent);
	}

	[Theory]
	[InlineData(StartTlsMode.Smtp, "454 TLS not available due to temporary reason", "The server refused STARTTLS", "454")]
	[InlineData(StartTlsMode.Imap, "a001 NO TLS not available", "The server refused STARTTLS", "a001 NO")]
	[InlineData(StartTlsMode.Ftp, "502 Command not implemented", "The server refused AUTH TLS", "502")]
	public async Task Exchange_RefusedStartTls_ThrowsWhatTheServerSaid(StartTlsMode mode, string refusal, string expected, string quoted)
	{
		FakeServer server = new([GreetingOf(mode), EhloReply(mode), refusal]);

		CertificateException ex = await Assert.ThrowsAsync<CertificateException>(
			() => StartTlsExchange.ExchangeAsync(mode, server.Stream, TestContext.Current.CancellationToken));

		Assert.StartsWith(expected, ex.Message, StringComparison.Ordinal);
		Assert.Contains(quoted, ex.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(StartTlsMode.Smtp, "450 graylisted, come back later", "The server's SMTP greeting was not 220")]
	[InlineData(StartTlsMode.Imap, "* BYE closing", "The server's IMAP greeting was not an OK")]
	[InlineData(StartTlsMode.Ftp, "421 too many connections", "The server's FTP greeting was not 220")]
	public async Task Exchange_WrongGreeting_ThrowsWhatTheServerSaid(StartTlsMode mode, string greeting, string expected)
	{
		FakeServer server = new([greeting]);

		CertificateException ex = await Assert.ThrowsAsync<CertificateException>(
			() => StartTlsExchange.ExchangeAsync(mode, server.Stream, TestContext.Current.CancellationToken));

		Assert.StartsWith(expected, ex.Message, StringComparison.Ordinal);
		Assert.Contains(greeting, ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Exchange_ServerGoesQuiet_ThrowsInWords()
	{
		FakeServer server = new(["220 mail.example.com ESMTP ready", "250 OK"]);

		CertificateException ex = await Assert.ThrowsAsync<CertificateException>(
			() => StartTlsExchange.ExchangeAsync(StartTlsMode.Smtp, server.Stream, TestContext.Current.CancellationToken));

		Assert.Equal("The server closed the connection while it was still speaking plain text.", ex.Message);
	}

	[Theory]
	[InlineData("220 Ready", true)]
	[InlineData("220-Ready", false)]
	[InlineData("220", true)]
	[InlineData("454 No", true)]
	public void IsFinalSmtpLine_KnowsTheHyphenRule(string line, bool final) => Assert.Equal(final, StartTlsExchange.IsFinalSmtpLine(line));

	[Fact]
	public void Command_IsEachModesOwn()
	{
		Assert.Equal("STARTTLS", StartTlsExchange.Command(StartTlsMode.Smtp));
		Assert.Equal("a001 STARTTLS", StartTlsExchange.Command(StartTlsMode.Imap));
		Assert.Equal("AUTH TLS", StartTlsExchange.Command(StartTlsMode.Ftp));
		Assert.Equal("", StartTlsExchange.Command(StartTlsMode.Plain));
	}

	private static string GreetingOf(StartTlsMode mode) => mode switch
	{
		StartTlsMode.Smtp => "220 mail.example.com ESMTP ready",
		StartTlsMode.Imap => "* OK ready",
		_ => "220 FTP server ready",
	};

	private static string? EhloReply(StartTlsMode mode) => mode == StartTlsMode.Smtp ? "250 mail.example.com hello" : null;

	private static string CommandReplyOf(StartTlsMode mode) => mode switch
	{
		StartTlsMode.Smtp => "220 Ready to start TLS",
		StartTlsMode.Imap => "a001 OK Begin TLS negotiation now",
		_ => "234 AUTH TLS successful",
	};

	private static IEnumerable<string> ExpectedLines(StartTlsMode mode) => mode switch
	{
		StartTlsMode.Smtp => ["EHLO mokaterm", "STARTTLS"],
		StartTlsMode.Imap => ["a001 STARTTLS"],
		_ => ["AUTH TLS"],
	};

	/// <summary>
	/// One end of an in-memory connection: the lines the server would send, readable in order until they run out and
	/// then closed, and every line the checker writes, collected with the CRLF the wire uses taken back off. A null
	/// line stands for a reply the mode does not speak.
	/// </summary>
	private sealed class FakeServer(params string?[] lines) : Stream
	{
		private readonly MemoryStream _incoming = new(Encoding.Latin1.GetBytes(string.Join("\r\n", lines.Where(line => line is not null).Select(line => line!)) + "\r\n"));
		private readonly StringBuilder _sent = new();

		public Stream Stream => this;

		public IReadOnlyList<string> Sent => _sent.Length == 0 ? [] : [.. _sent.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries)];

		public override bool CanRead => true;

		public override bool CanWrite => true;

		public override bool CanSeek => false;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
			_incoming.ReadAsync(buffer, cancellationToken);

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
		{
			_sent.Append(Encoding.Latin1.GetString(buffer.Span));
			return ValueTask.CompletedTask;
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count) => _incoming.Read(buffer, offset, count);

		public override void Write(byte[] buffer, int offset, int count) => _sent.Append(Encoding.Latin1.GetString(buffer, offset, count));

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
