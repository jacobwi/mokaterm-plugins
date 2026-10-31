using Moka.Certificates.Crypto;
using Moka.Red.Core.Enums;

namespace Moka.Certificates.Components;

/// <summary>The color a certificate's expiry state is drawn in, in chips and dots alike.</summary>
public static class ExpiryColor
{
	public static MokaColor Of(ExpiryState state) => state switch
	{
		ExpiryState.Expired or ExpiryState.NotYetValid => MokaColor.Error,
		ExpiryState.Expiring => MokaColor.Warning,
		_ => MokaColor.Success,
	};
}
