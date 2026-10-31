namespace Moka.Certificates.Tls;

/// <summary>
/// Where the TLS handshake starts: at once on a TLS port, or after the STARTTLS exchange another protocol defines on a
/// plain port. The mode changes what the check speaks before it handshakes, and the port it connects to by default.
/// </summary>
public enum StartTlsMode
{
	/// <summary>TLS from the first byte, as HTTPS, SMTPS and IMAPS do.</summary>
	Plain,

	/// <summary>SMTP (RFC 3207): a greeting, EHLO, then STARTTLS.</summary>
	Smtp,

	/// <summary>IMAP (RFC 3501): a greeting, then one tagged STARTTLS command.</summary>
	Imap,

	/// <summary>FTP (RFC 4217): a greeting, then AUTH TLS.</summary>
	Ftp,
}
