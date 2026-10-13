namespace Moka.Certificates.Crypto;

/// <summary>One PEM block as DER: its label, its bytes and the file extension that suits it.</summary>
public sealed record DerBlock(string Label, byte[] Der, string FileExtension)
{
	/// <summary>The block may hold a private key, so it is saved and never shown.</summary>
	public bool IsSecret => Label.Contains("PRIVATE", StringComparison.Ordinal);
}
