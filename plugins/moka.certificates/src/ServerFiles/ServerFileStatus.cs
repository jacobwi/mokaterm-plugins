namespace Moka.Certificates.ServerFiles;

/// <summary>How reading one certificate file went.</summary>
public enum ServerFileStatus
{
	Read,
	Missing,
	Unreadable,
	Folder,

	/// <summary>Read, but nothing in it was a certificate (a binary file too large to be one, or text without a certificate block).</summary>
	NoCertificate,

	/// <summary>The read script said nothing about the file, which happens when the output was cut at its cap.</summary>
	NotRead,
}
