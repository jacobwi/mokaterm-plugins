namespace Moka.Certificates.Crypto;

/// <summary>Where a certificate stands against its validity dates, with expiring meaning within the days set in Settings.</summary>
public enum ExpiryState
{
	Valid,
	Expiring,
	Expired,
	NotYetValid,
}
