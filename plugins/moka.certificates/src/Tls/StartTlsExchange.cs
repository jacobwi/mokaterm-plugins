using System.Text;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tls;

/// <summary>
/// The lines spoken before the TLS handshake in a STARTTLS mode: SMTP reads a greeting, sends EHLO and reads its
/// multiline reply, then sends STARTTLS; IMAP reads a greeting and sends one tagged STARTTLS, skipping any untagged
/// lines that come first; FTP reads a greeting and sends AUTH TLS. Every line is judged by a pure rule, so the exchange
/// is tested against scripted replies without a network; a server that answers out of turn is a
/// <see cref="CertificateException"/> that quotes what it said.
/// </summary>
public static class StartTlsExchange
{
	/// <summary>The host name the EHLO greeting carries: SMTP wants one, and this checker is that name.</summary>
	public const string EhloName = "mokaterm";

	/// <summary>The command that asks the server to switch to TLS. Empty for a mode that only reads a greeting.</summary>
	public static string Command(StartTlsMode mode) => mode switch
	{
		StartTlsMode.Smtp => "STARTTLS",
		StartTlsMode.Imap => "a001 STARTTLS",
		StartTlsMode.Ftp => "AUTH TLS",
		_ => "",
	};

	/// <summary>Speaks the mode's lines on the stream. Does nothing in the plain mode; throws in words when the server does not answer like the mode or refuses TLS.</summary>
	/// <exception cref="CertificateException">The server's lines were not what the mode speaks.</exception>
	public static async Task ExchangeAsync(StartTlsMode mode, Stream stream, CancellationToken cancellationToken)
	{
		if (mode == StartTlsMode.Plain)
		{
			return;
		}

		// Latin-1 passes every byte through, so a reply's own text survives into the error that quotes it. Both leave
		// the stream open: the TLS handshake follows over the same connection.
		using StreamReader reader = new(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, bufferSize: 512, leaveOpen: true);
		using StreamWriter writer = new(stream, Encoding.Latin1, bufferSize: 256, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

		string? greeting = mode == StartTlsMode.Smtp
			? await ReadSmtpReplyAsync(reader, cancellationToken)
			: await reader.ReadLineAsync(cancellationToken);
		Throw(GreetingProblem(mode, greeting));

		if (mode == StartTlsMode.Smtp)
		{
			await writer.WriteLineAsync($"EHLO {EhloName}".AsMemory(), cancellationToken);
			Throw(EhloProblem(await ReadSmtpReplyAsync(reader, cancellationToken)));
		}

		await writer.WriteLineAsync(Command(mode).AsMemory(), cancellationToken);
		string? reply = mode == StartTlsMode.Imap
			? await ReadImapReplyAsync(reader, cancellationToken)
			: await ReadSmtpReplyAsync(reader, cancellationToken);
		Throw(ReplyProblem(mode, reply));
	}

	/// <summary>Reads an SMTP reply to its last line: one that ends it has no hyphen in fourth place.</summary>
	private static async Task<string?> ReadSmtpReplyAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		string? line = await reader.ReadLineAsync(cancellationToken);
		while (line is not null && !IsFinalSmtpLine(line))
		{
			line = await reader.ReadLineAsync(cancellationToken);
		}

		return line;
	}

	/// <summary>Reads an IMAP reply: any untagged lines first, then the one that carries this checker's tag.</summary>
	private static async Task<string?> ReadImapReplyAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		string? line = await reader.ReadLineAsync(cancellationToken);
		while (line is not null && !line.StartsWith("a001", StringComparison.OrdinalIgnoreCase))
		{
			line = await reader.ReadLineAsync(cancellationToken);
		}

		return line;
	}

	/// <summary>Whether an SMTP reply line ends its reply: the fourth character, when there is one, is not a hyphen.</summary>
	public static bool IsFinalSmtpLine(string line) => line.Length < 4 || line[3] != '-';

	/// <summary>Why the greeting is not what the mode speaks, or null when it is.</summary>
	public static string? GreetingProblem(StartTlsMode mode, string? line) => mode switch
	{
		StartTlsMode.Smtp => SmtpProblem("The server's SMTP greeting was not 220.", "220", line),
		StartTlsMode.Imap => line is null ? ClosedProblem
			: line.StartsWith("* OK", StringComparison.OrdinalIgnoreCase) ? null
			: Quoted("The server's IMAP greeting was not an OK.", line),
		StartTlsMode.Ftp => SmtpProblem("The server's FTP greeting was not 220.", "220", line),
		_ => null,
	};

	/// <summary>Why the server's reply to EHLO is no good, or null when it accepts.</summary>
	public static string? EhloProblem(string? line) => SmtpProblem("The server did not accept EHLO.", "250", line);

	/// <summary>Why the reply to the mode's own command refuses TLS, or null when it agrees to switch.</summary>
	public static string? ReplyProblem(StartTlsMode mode, string? line) => mode switch
	{
		StartTlsMode.Smtp => SmtpProblem("The server refused STARTTLS.", "220", line),
		StartTlsMode.Imap => line is null ? ClosedProblem
			: ImapAccepted(line) ? null
			: Quoted("The server refused STARTTLS.", line),
		StartTlsMode.Ftp => SmtpProblem("The server refused AUTH TLS.", "234", line),
		_ => null,
	};

	/// <summary>Why an SMTP-style reply does not carry the code wanted, or null when it does.</summary>
	private static string? SmtpProblem(string what, string code, string? line) => line switch
	{
		null => ClosedProblem,
		_ when Code(line) == code => null,
		_ when Code(line) is null => Quoted("The server's reply could not be read.", line),
		_ => Quoted(what, line),
	};

	/// <summary>An SMTP reply's three-digit code, or null when the line does not start with one.</summary>
	private static string? Code(string line) =>
		line.Length >= 3 && char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]) && char.IsAsciiDigit(line[2]) ? line[..3] : null;

	/// <summary>Whether the tagged reply says OK: the tag this checker sent, then the word OK.</summary>
	private static bool ImapAccepted(string line)
	{
		string[] words = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		return words.Length >= 2 && words[0].Equals("a001", StringComparison.OrdinalIgnoreCase) && words[1].Equals("OK", StringComparison.OrdinalIgnoreCase);
	}

	private static void Throw(string? problem)
	{
		if (problem is not null)
		{
			throw new CertificateException(problem);
		}
	}

	private static string ClosedProblem => "The server closed the connection while it was still speaking plain text.";

	private static string Quoted(string what, string line) =>
		line.Length > 120 ? $"{what} It said: \"{line[..120]}...\"." : $"{what} It said: \"{line}\".";
}
