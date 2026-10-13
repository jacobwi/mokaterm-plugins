namespace Moka.Certificates.Crypto;

/// <summary>One name from a certificate's names, such as <c>DNS www.example.com</c>.</summary>
public sealed record GeneralNameInfo(GeneralNameKind Kind, string Value)
{
	/// <summary>The short tag the name is shown with: DNS, IP, email, URI, DIR, RID, other.</summary>
	public string Tag => Kind switch
	{
		GeneralNameKind.Dns => "DNS",
		GeneralNameKind.IpAddress => "IP",
		GeneralNameKind.Email => "email",
		GeneralNameKind.Uri => "URI",
		GeneralNameKind.DirectoryName => "DIR",
		GeneralNameKind.RegisteredId => "RID",
		_ => "other",
	};

	public override string ToString() => $"{Tag}:{Value}";
}
