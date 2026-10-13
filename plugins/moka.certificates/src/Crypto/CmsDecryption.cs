using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>What a CMS enveloped message held, once decrypted.</summary>
public sealed record CmsDecryption(byte[] Content, int RecipientCount)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Content = ").Append(Content.Length).Append(" bytes");
		return true;
	}
}
