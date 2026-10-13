using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>How a new PFX is protected and labeled.</summary>
public sealed record PfxOptions
{
	public string Password { get; init; } = "";

	/// <summary>The name Windows' certificate manager shows for it; empty leaves it out.</summary>
	public string FriendlyName { get; init; } = "";

	public PfxEncryption Encryption { get; init; } = PfxEncryption.Aes256;

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Encryption = ").Append(Encryption).Append(", Password = ").Append(Password.Length > 0 ? "(set)" : "(empty)");
		return true;
	}
}
