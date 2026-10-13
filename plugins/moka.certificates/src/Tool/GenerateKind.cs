namespace Moka.Certificates.Tool;

/// <summary>What the Generate section makes.</summary>
public enum GenerateKind
{
	KeyPair,
	Request,
	SelfSigned,
	Authority,
	SignedByCa,
}
