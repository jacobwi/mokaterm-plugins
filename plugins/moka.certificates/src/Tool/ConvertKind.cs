namespace Moka.Certificates.Tool;

/// <summary>The conversions the Convert section offers.</summary>
public enum ConvertKind
{
	PemToDer,
	DerToPem,
	PfxToPem,
	PemToPfx,
	KeyFormat,
	EncryptKey,
	DecryptKey,
}
